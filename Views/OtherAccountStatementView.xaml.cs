using FinanceApp.Data;
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
    // Similar to CreditCardStatementView, but Debit/Credit columns replace
    // Charge/Payment (DEBIT -> Debit, CREDIT -> Credit) and the running
    // balance formula is previous + Debit - Credit.
    public partial class OtherAccountStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        private readonly List<TransactionRowViewModel> _allRows = new();
        private ObservableCollection<TransactionRowViewModel> _view = new();

        public ObservableCollection<string> Categories { get; private set; } = new();

        private bool _hasChanges;

        public OtherAccountStatementView(DatabaseManager db, string account)
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

                // Debit (Payment field) = DEBIT, Credit (Deposit field) = CREDIT
                if (effectiveTrn == "DEBIT")
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

        // Running balance = previous + Debit(Payment) - Credit(Deposit)
        private void RecalcBalance()
        {
            decimal running = 0;
            foreach (var row in _view)
            {
                running += row.PaymentDecimal - row.DepositDecimal;
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
            LoadData();
        }

        private void SaveRow(SqliteConnection conn, TransactionRowViewModel row)
        {
            var cleanMemo = CleanText(row.Memo);
            var date      = row.DateValue?.ToString("yyyy-MM-dd") ?? "";

            // Payment field = Debit (DEBIT), Deposit field = Credit (CREDIT)
            bool hasDebit  = row.PaymentDecimal != 0;
            decimal amount = hasDebit ? row.PaymentDecimal : row.DepositDecimal;

            // Unswap for category-matched rows before storing
            string trnType;
            if (row.MatchedViaCategory)
                trnType = hasDebit ? "CREDIT" : "DEBIT";
            else
                trnType = hasDebit ? "DEBIT" : "CREDIT";

            string dbCategory = row.MatchedViaCategory ? _account : row.DisplayCategory;

            if (row.IsNew)
            {
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, Name, Category, Clear, Amount, Memo, FinancialId, TrnType)
                    VALUES
                        (@acct,@date,@name,@cat,@clr,@amt,@memo,@fid,@trn);", conn);
                cmd.Parameters.AddWithValue("@acct", _account);
                cmd.Parameters.AddWithValue("@date", date);
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
                        Date=@date, Name=@name, Category=@cat,
                        Clear=@clr, Amount=@amt, Memo=@memo, FinancialId=@fid, TrnType=@trn
                    WHERE rowid=@rid;", conn);
                cmd.Parameters.AddWithValue("@date", date);
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

            // Update CatHistory keyed on Memo for account-matched rows
            if (!row.MatchedViaCategory
                && !string.IsNullOrWhiteSpace(cleanMemo)
                && !string.IsNullOrWhiteSpace(row.DisplayCategory))
            {
                UpdateCatHistory(conn, cleanMemo, row.DisplayCategory);
            }

            row.OriginalCategory = row.DisplayCategory;
            row.IsDirty = false;
            row.IsNew   = false;
        }

        private static void UpdateCatHistory(SqliteConnection conn, string description, string category)
        {
            using var chk = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description=@d;", conn);
            chk.Parameters.AddWithValue("@d", description);
            var existing = chk.ExecuteScalar() as string;

            if (existing is null)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO CatHistory(Description,Category,Amount) VALUES(@d,@c,0);", conn);
                ins.Parameters.AddWithValue("@d", description);
                ins.Parameters.AddWithValue("@c", category);
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

            // When the Category combo commits, sync CatHistory using the row's Memo.
            if (e.Column == TrGrid.Columns[2]
                && e.Row.DataContext is TransactionRowViewModel row
                && !row.MatchedViaCategory)
            {
                var cleanMemo = CleanText(row.Memo);
                var category  = row.DisplayCategory;
                if (!string.IsNullOrWhiteSpace(cleanMemo) && !string.IsNullOrWhiteSpace(category))
                {
                    using var conn = _db.OpenConnection();
                    UpdateCatHistory(conn, cleanMemo, category);
                }
            }

            Dispatcher.BeginInvoke(new Action(RecalcBalance));
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
