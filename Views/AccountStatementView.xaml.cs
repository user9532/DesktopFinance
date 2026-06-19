using FinanceApp.Data;
using FinanceApp.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FinanceApp.Views
{
    public partial class AccountStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        private readonly List<TransactionRowViewModel> _allRows = new();
        private ObservableCollection<TransactionRowViewModel> _view = new();

        public ObservableCollection<string> Categories { get; private set; } = new();

        private bool _hasChanges;
        private bool _suppressWarning;

        public AccountStatementView(DatabaseManager db, string account)
        {
            // Assign before InitializeComponent so SelectionChanged fired
            // during XAML init (SelectedIndex="0") finds valid state.
            _db      = db;
            _account = account;
            InitializeComponent();
            LoadCategories();
            LoadData();
        }

        // ── category list ────────────────────────────────────────────────

        private void LoadCategories()
        {
            Categories.Clear();
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT AccountName FROM AccountChart
                WHERE AccountName != @acct
                ORDER BY
                    CASE AccountGroup
                        WHEN 'Expense'   THEN 1
                        WHEN 'Income'    THEN 2
                        WHEN 'Asset'     THEN 3
                        WHEN 'Liability' THEN 4
                        ELSE 5
                    END,
                    AccountName;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Categories.Add(r.GetString(0));
        }

        public void RefreshCategories() => LoadCategories();

        // ── data load ────────────────────────────────────────────────────

        private void LoadData()
        {
            _allRows.Clear();
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT rowid, Account, Date, CheckNo, Name, Category,
                       Clear, Amount, Memo, FinancialId, TrnType
                FROM   Transactions
                WHERE  Account = @acct OR Category = @acct
                ORDER  BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                var acctVal  = r.IsDBNull(1)  ? "" : r.GetString(1);
                var category = r.IsDBNull(5)  ? "" : r.GetString(5);
                var amount   = r.IsDBNull(7)  ? 0m : (decimal)r.GetDouble(7);
                var trnType  = r.IsDBNull(10) ? "" : r.GetString(10);
                var viacat   = acctVal != _account;      // matched via Category column

                // Swap DEBIT/CREDIT when viewed from the category side
                var effectiveTrn = viacat
                    ? (trnType == "DEBIT" ? "CREDIT" : "DEBIT")
                    : trnType;

                var row = new TransactionRowViewModel();
                row.BeginLoad();

                row.RowId              = r.GetInt64(0);
                row.IsNew              = false;
                row.MatchedViaCategory = viacat;
                row.AccountValue       = acctVal;
                row.TrnType            = trnType;
                row.DateValue          = DateTime.TryParse(r.IsDBNull(2) ? "" : r.GetString(2), out var d) ? d : null;
                row.CheckNo            = r.IsDBNull(3) ? "" : r.GetString(3);
                row.Name               = r.IsDBNull(4) ? "" : r.GetString(4);
                row.Clear              = !r.IsDBNull(6) && r.GetInt32(6) != 0;
                row.Memo               = r.IsDBNull(8) ? "" : r.GetString(8);
                row.FinancialId        = r.IsDBNull(9) ? "" : r.GetString(9);

                if (effectiveTrn == "CREDIT")
                    row.Payment = amount > 0 ? amount.ToString("N2") : "";
                else
                    row.Deposit = amount > 0 ? amount.ToString("N2") : "";

                // Category-matched rows: show the Account value in the Category column
                row.DisplayCategory  = viacat ? acctVal : category;
                row.OriginalCategory = row.DisplayCategory;

                row.EndLoad();
                _allRows.Add(row);
            }

            ApplyFilter();
        }

        // ── filter & balance ─────────────────────────────────────────────

        private void ApplyFilter()
        {
            // TrGrid is null when SelectionChanged fires during InitializeComponent
            // (CbFilter is created before TrGrid in the XAML). Skip until fully loaded.
            if (TrGrid == null) return;

            var filter = (CbFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()
                         ?? "All Transactions";

            IEnumerable<TransactionRowViewModel> src = _allRows;
            if (filter == "Cleared")   src = src.Where(x => x.Clear);
            if (filter == "Uncleared") src = src.Where(x => !x.Clear);

            _view = new ObservableCollection<TransactionRowViewModel>(src);
            RecalcBalance();
            TrGrid.ItemsSource = _view;
        }

        private void RecalcBalance()
        {
            decimal running = 0;
            foreach (var row in _view)
            {
                running += row.DepositDecimal - row.PaymentDecimal;
                row.Balance = running;
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ApplyFilter();

        // ── add row ───────────────────────────────────────────────────────

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var row = new TransactionRowViewModel
            {
                IsNew        = true,
                AccountValue = _account,
                DateValue    = DateTime.Today,
                IsDirty      = true
            };
            _allRows.Add(row);
            _view.Add(row);
            RecalcBalance();
            _hasChanges = true;
            TrGrid.ScrollIntoView(row);
            TrGrid.SelectedItem = row;
            TrGrid.CurrentCell  = new DataGridCellInfo(row, TrGrid.Columns[0]);
            TrGrid.BeginEdit();
        }

        // ── save ──────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e) => SaveAll();

        private void SaveAll()
        {
            var toSave = _allRows.Where(r => r.IsDirty || r.IsNew).ToList();
            if (!toSave.Any()) return;

            using var conn = _db.OpenConnection();
            foreach (var row in toSave)
                SaveRow(conn, row);

            _hasChanges = false;
            // Reload to get accurate rowids for inserted rows and refresh display
            LoadData();
        }

        private void SaveRow(SqliteConnection conn, TransactionRowViewModel row)
        {
            var cleanMemo = CleanText(row.Memo);
            var date      = row.DateValue?.ToString("yyyy-MM-dd") ?? "";

            // Determine amount & TrnType from Payment/Deposit strings
            bool hasPayment = row.PaymentDecimal != 0;
            decimal amount  = hasPayment ? row.PaymentDecimal : row.DepositDecimal;

            // Unswap for category-matched rows before storing
            string trnType;
            if (row.MatchedViaCategory)
                trnType = hasPayment ? "DEBIT" : "CREDIT";
            else
                trnType = hasPayment ? "CREDIT" : "DEBIT";

            // For account-matched rows the DisplayCategory IS the category to store.
            // For category-matched rows the Category column keeps the selected account;
            // the Account column is stored as-is (we don't change other account's Account field).
            string dbCategory = row.MatchedViaCategory ? _account : row.DisplayCategory;

            if (row.IsNew)
            {
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, CheckNo, Name, Category, Clear, Amount, Memo, FinancialId, TrnType)
                    VALUES
                        (@acct,@date,@chk,@name,@cat,@clr,@amt,@memo,@fid,@trn);", conn);
                cmd.Parameters.AddWithValue("@acct", _account);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@chk",  row.CheckNo ?? "");
                cmd.Parameters.AddWithValue("@name", row.Name ?? "");
                cmd.Parameters.AddWithValue("@cat",  dbCategory ?? "");
                cmd.Parameters.AddWithValue("@clr",  row.Clear ? 1 : 0);
                cmd.Parameters.AddWithValue("@amt",  (double)amount);
                cmd.Parameters.AddWithValue("@memo", cleanMemo);
                cmd.Parameters.AddWithValue("@fid",  row.FinancialId ?? "");
                cmd.Parameters.AddWithValue("@trn",  trnType);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new SqliteCommand(@"
                    UPDATE Transactions SET
                        Date=@date, CheckNo=@chk, Name=@name, Category=@cat,
                        Clear=@clr, Amount=@amt, Memo=@memo, FinancialId=@fid, TrnType=@trn
                    WHERE rowid=@rid;", conn);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@chk",  row.CheckNo ?? "");
                cmd.Parameters.AddWithValue("@name", row.Name ?? "");
                cmd.Parameters.AddWithValue("@cat",  dbCategory ?? "");
                cmd.Parameters.AddWithValue("@clr",  row.Clear ? 1 : 0);
                cmd.Parameters.AddWithValue("@amt",  (double)amount);
                cmd.Parameters.AddWithValue("@memo", cleanMemo);
                cmd.Parameters.AddWithValue("@fid",  row.FinancialId ?? "");
                cmd.Parameters.AddWithValue("@trn",  trnType);
                cmd.Parameters.AddWithValue("@rid",  row.RowId);
                cmd.ExecuteNonQuery();
            }

            // Update CatHistory keyed on Name when an account-matched row has a name and category
            var cleanName = CleanText(row.Name);
            if (!row.MatchedViaCategory
                && !string.IsNullOrWhiteSpace(cleanName)
                && !string.IsNullOrWhiteSpace(row.DisplayCategory))
            {
                UpdateCatHistory(conn, cleanName, row.DisplayCategory, amount);
            }

            row.OriginalCategory = row.DisplayCategory;
            row.IsDirty = false;
            row.IsNew   = false;
        }

        private static void UpdateCatHistory(SqliteConnection conn, string name, string category, decimal amount)
        {
            using var chk = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description=@d;", conn);
            chk.Parameters.AddWithValue("@d", name);
            var existing = chk.ExecuteScalar() as string;

            if (existing is null)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO CatHistory(Description,Category,Amount) VALUES(@d,@c,@a);", conn);
                ins.Parameters.AddWithValue("@d", name);
                ins.Parameters.AddWithValue("@c", category);
                ins.Parameters.AddWithValue("@a", (double)amount);
                ins.ExecuteNonQuery();
            }
            else
            {
                using var upd = new SqliteCommand(
                    "UPDATE CatHistory SET Category=@c, Amount=@a WHERE Description=@d;", conn);
                upd.Parameters.AddWithValue("@d", name);
                upd.Parameters.AddWithValue("@c", category);
                upd.Parameters.AddWithValue("@a", (double)amount);
                upd.ExecuteNonQuery();
            }
        }

        private static string CleanText(string? s) =>
            string.IsNullOrWhiteSpace(s) ? "" : Regex.Replace(s.Trim(), @"\s+", " ");

        // ── grid events ───────────────────────────────────────────────────

        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            _hasChanges = true;

            // When the Name column commits on a new row, look up CatHistory
            // and auto-fill Category and Payment/Deposit from the last known entry.
            if (e.Column == TrGrid.Columns[2]
                && e.Row.DataContext is TransactionRowViewModel row
                && row.IsNew)
            {
                Dispatcher.BeginInvoke(new Action(() => AutoFillFromCatHistory(row)));
            }

            // Recalculate balance after any edit (Payment/Deposit may have changed)
            Dispatcher.BeginInvoke(new Action(RecalcBalance));
        }

        private void AutoFillFromCatHistory(TransactionRowViewModel row)
        {
            var name = row.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Category, Amount FROM CatHistory WHERE Description = @d LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@d", name);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;

            var cat = r.IsDBNull(0) ? "" : r.GetString(0);
            var amt = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);

            if (!string.IsNullOrEmpty(cat))
                row.DisplayCategory = cat;

            if (amt > 0 && row.PaymentDecimal == 0 && row.DepositDecimal == 0)
                row.Payment = amt.ToString("N2");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _hasChanges = true;
        }

        private void DatePicker_Loaded(object sender, RoutedEventArgs e) { }

        private void CategoryCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb)
                cb.ItemsSource = Categories;
        }

        // ── right-click context menu ──────────────────────────────────────

        // Selects the DataGridRow under the cursor before the context menu opens.
        private void TrGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.DataContext is TransactionRowViewModel vm)
                TrGrid.SelectedItem = vm;
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (TrGrid.SelectedItem is not TransactionRowViewModel row) return;

            var confirm = MessageBox.Show(
                "Are you sure you want to delete this transaction?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            if (!row.IsNew)
            {
                using var conn = _db.OpenConnection();
                using var cmd = new SqliteCommand(
                    "DELETE FROM Transactions WHERE rowid = @id;", conn);
                cmd.Parameters.AddWithValue("@id", row.RowId);
                cmd.ExecuteNonQuery();
            }

            _allRows.Remove(row);
            _view.Remove(row);
            RecalcBalance();
        }

        private void SplitRow_Click(object sender, RoutedEventArgs e)
        {
            if (TrGrid.SelectedItem is not TransactionRowViewModel row) return;

            if (row.IsNew)
            {
                MessageBox.Show("Save new rows before splitting.",
                    "Cannot Split", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (row.MatchedViaCategory)
            {
                MessageBox.Show(
                    "Split is not available for transactions matched via the Category column.",
                    "Cannot Split", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Auto-save any pending edits so the DB is consistent before the split
            if (_hasChanges) SaveAll();

            var dlg = new SplitTransactionDialog(_db, row, Categories, _account)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true)
                LoadData();   // Reload fresh from DB after the split
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        // ── window lifecycle / unsaved-changes warnings ───────────────────

        private void View_Loaded(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null) w.Deactivated += Window_Deactivated;
        }

        private void View_Unloaded(object sender, RoutedEventArgs e)
        {
            var w = Window.GetWindow(this);
            if (w != null) w.Deactivated -= Window_Deactivated;
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (!_hasChanges || _suppressWarning) return;
            _suppressWarning = true;
            var r = MessageBox.Show(
                "There are unsaved changes. Do you want to save them now?",
                "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) SaveAll();
            _suppressWarning = false;
        }

        // ── public surface used by MainWindow closing handler ─────────────

        public bool HasUnsavedChanges => _hasChanges;

        public void SaveChanges() => SaveAll();

        // Reloads data from the database (called when the tab is re-activated).
        public void Refresh() => LoadData();

        // Lets the MainWindow suppress the deactivation-warning while its
        // own "unsaved changes" dialog is open, preventing a second popup.
        public void SuppressWarning(bool suppress) => _suppressWarning = suppress;

        // Called by the tab's close button before removing the tab
        public bool CanClose()
        {
            if (!_hasChanges) return true;

            // Suppress Window_Deactivated: showing this MessageBox steals focus from the
            // main window, which would fire Window_Deactivated and produce a second warning.
            _suppressWarning = true;
            var r = MessageBox.Show(
                "There are unsaved changes. Do you want to save before closing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            _suppressWarning = false;

            if (r == MessageBoxResult.Yes) { SaveAll(); return true; }
            return r == MessageBoxResult.No;
        }
    }
}
