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
    // Similar to AccountStatementView, but Charge/Payment columns replace
    // Payment/Deposit (CREDIT -> Charge, DEBIT -> Payment) and CheckNo/Split
    // are not applicable to credit card accounts.
    public partial class CreditCardStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public string Account => _account;

        private readonly List<TransactionRowViewModel> _allRows = new();
        private ObservableCollection<TransactionRowViewModel> _view = new();

        public ObservableCollection<string> Categories { get; private set; } = new();

        private bool _hasChanges;

        public CreditCardStatementView(DatabaseManager db, string account)
        {
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
                SELECT rowid, Account, Date, Name, Category,
                       Clear, Amount, Memo, FinancialId, TrnType
                FROM   Transactions
                WHERE  Account = @acct OR Category = @acct
                ORDER  BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                var acctVal  = r.IsDBNull(1) ? "" : r.GetString(1);
                var category = r.IsDBNull(4) ? "" : r.GetString(4);
                var amount   = r.IsDBNull(6) ? 0m : (decimal)r.GetDouble(6);
                var trnType  = r.IsDBNull(9) ? "" : r.GetString(9);
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
                row.Name               = r.IsDBNull(3) ? "" : r.GetString(3);
                row.Clear              = !r.IsDBNull(5) && r.GetInt32(5) != 0;
                row.Memo               = r.IsDBNull(7) ? "" : r.GetString(7);
                row.FinancialId        = r.IsDBNull(8) ? "" : r.GetString(8);

                // Charge (Payment field) = CREDIT, Payment (Deposit field) = DEBIT
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

        // Running balance = previous + Payment(DEBIT) - Charge(CREDIT)
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
            TrGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TrGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var toSave = _allRows.Where(r => r.IsDirty || r.IsNew).ToList();
            if (!toSave.Any()) return;

            using var conn = _db.OpenConnection();
            foreach (var row in toSave)
                SaveRow(conn, row);

            _hasChanges = false;
            LoadData();
        }

        private void SaveRow(SqliteConnection conn, TransactionRowViewModel row)
        {
            var cleanMemo = CleanText(row.Memo);
            var cleanName = CleanText(row.Name);
            var date      = row.DateValue?.ToString("yyyy-MM-dd") ?? "";

            // Payment field = Charge (CREDIT), Deposit field = Payment (DEBIT)
            bool hasCharge = row.PaymentDecimal != 0;
            decimal amount = hasCharge ? row.PaymentDecimal : row.DepositDecimal;

            // Unswap for category-matched rows before storing
            string trnType;
            if (row.MatchedViaCategory)
                trnType = hasCharge ? "DEBIT" : "CREDIT";
            else
                trnType = hasCharge ? "CREDIT" : "DEBIT";

            string dbCategory = row.MatchedViaCategory ? _account : row.DisplayCategory;
            string dbAccount  = row.MatchedViaCategory ? row.DisplayCategory : _account;

            if (row.IsNew)
            {
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, Name, Category, Clear, Amount, Memo, FinancialId, TrnType)
                    VALUES
                        (@acct,@date,@name,@cat,@clr,@amt,@memo,@fid,@trn);", conn);
                cmd.Parameters.AddWithValue("@acct", dbAccount);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@name", cleanName);
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
                        Account=@acct, Date=@date, Name=@name, Category=@cat,
                        Clear=@clr, Amount=@amt, Memo=@memo, FinancialId=@fid, TrnType=@trn
                    WHERE rowid=@rid;", conn);
                cmd.Parameters.AddWithValue("@acct", dbAccount);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@name", cleanName);
                cmd.Parameters.AddWithValue("@cat",  dbCategory ?? "");
                cmd.Parameters.AddWithValue("@clr",  row.Clear ? 1 : 0);
                cmd.Parameters.AddWithValue("@amt",  (double)amount);
                cmd.Parameters.AddWithValue("@memo", cleanMemo);
                cmd.Parameters.AddWithValue("@fid",  row.FinancialId ?? "");
                cmd.Parameters.AddWithValue("@trn",  trnType);
                cmd.Parameters.AddWithValue("@rid",  row.RowId);
                cmd.ExecuteNonQuery();
            }

            // Update CatHistory keyed on Name for account-matched rows
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

        private static void UpdateCatHistory(SqliteConnection conn, string description, string category, decimal amount = 0)
        {
            using var chk = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description=@d;", conn);
            chk.Parameters.AddWithValue("@d", description);
            var existing = chk.ExecuteScalar() as string;

            if (existing is null)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO CatHistory(Description,Category,Amount) VALUES(@d,@c,@a);", conn);
                ins.Parameters.AddWithValue("@d", description);
                ins.Parameters.AddWithValue("@c", category);
                ins.Parameters.AddWithValue("@a", (double)amount);
                ins.ExecuteNonQuery();
            }
            else if (existing != category)
            {
                using var upd = new SqliteCommand(
                    "UPDATE CatHistory SET Category=@c WHERE Description=@d;", conn);
                upd.Parameters.AddWithValue("@d", description);
                upd.Parameters.AddWithValue("@c", category);
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
            // and auto-fill Category and Amount from the last known entry.
            if (e.Column == TrGrid.Columns[1]
                && e.Row.DataContext is TransactionRowViewModel newRow
                && newRow.IsNew)
            {
                Dispatcher.BeginInvoke(new Action(() => AutoFillFromCatHistory(newRow)));
            }

            // When the Category combo commits, sync CatHistory using the row's Name.
            if (e.Column == TrGrid.Columns[2]
                && e.Row.DataContext is TransactionRowViewModel row
                && !row.MatchedViaCategory)
            {
                var cleanName = CleanText(row.Name);
                var category  = row.DisplayCategory;
                if (!string.IsNullOrWhiteSpace(cleanName) && !string.IsNullOrWhiteSpace(category))
                {
                    using var conn = _db.OpenConnection();
                    UpdateCatHistory(conn, cleanName, category);
                }
            }

            Dispatcher.BeginInvoke(new Action(RecalcBalance));
        }

        private void AutoFillFromCatHistory(TransactionRowViewModel row)
        {
            var name = CleanText(row.Name);
            if (string.IsNullOrEmpty(name)) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Category, Amount FROM CatHistory WHERE Description = @d LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@d", name);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                row.NameMatchedCatHistory = false;
                return;
            }

            row.NameMatchedCatHistory = true;

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

        // Preserves an existing multi-selection when right-clicking a row that's
        // already part of it (standard Windows Explorer behavior).
        private void TrGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.DataContext is TransactionRowViewModel vm && !TrGrid.SelectedItems.Contains(vm))
                TrGrid.SelectedItem = vm;
        }

        // Disables Split when more than one row is selected.
        private void TrGridContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            MiSplit.IsEnabled = TrGrid.SelectedItems.Count <= 1;
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var rows = TrGrid.SelectedItems.Cast<TransactionRowViewModel>().ToList();
            if (rows.Count == 0) return;

            var confirm = MessageBox.Show(
                rows.Count == 1
                    ? "Are you sure you want to delete this transaction?"
                    : $"Are you sure you want to delete these {rows.Count} transactions?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            using var conn = _db.OpenConnection();
            foreach (var row in rows)
            {
                if (!row.IsNew)
                {
                    using var cmd = new SqliteCommand(
                        "DELETE FROM Transactions WHERE rowid = @id;", conn);
                    cmd.Parameters.AddWithValue("@id", row.RowId);
                    cmd.ExecuteNonQuery();
                }

                _allRows.Remove(row);
                _view.Remove(row);
            }

            RecalcBalance();
        }

        private void ToggleClear_Click(object sender, RoutedEventArgs e)
        {
            var rows = TrGrid.SelectedItems.Cast<TransactionRowViewModel>().ToList();
            if (rows.Count == 0) return;

            bool newValue = !rows[0].Clear;
            foreach (var row in rows)
                row.Clear = newValue;
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

        // ── public surface used by MainWindow closing handler ─────────────

        public bool HasUnsavedChanges => _hasChanges;

        public void SaveChanges() => SaveAll();

        public void Refresh() => LoadData();

        public bool CanClose()
        {
            if (!_hasChanges) return true;

            var r = MessageBox.Show(
                "There are unsaved changes. Do you want to save before closing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes) { SaveAll(); return true; }
            return r == MessageBoxResult.No;
        }
    }
}
