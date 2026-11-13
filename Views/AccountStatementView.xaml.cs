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

        public string Account => _account;

        private readonly List<TransactionRowViewModel> _allRows = new();
        private ObservableCollection<TransactionRowViewModel> _view = new();

        public ObservableCollection<string> Categories { get; private set; } = new();

        private bool _hasChanges;

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
            TrGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TrGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var toSave = _allRows.Where(r => r.IsDirty || r.IsNew).ToList();
            if (!toSave.Any()) return;

            using var conn = _db.OpenConnection();
            foreach (var row in toSave)
                SaveRow(conn, row);

            _hasChanges = false;
            // Reload to get accurate rowids for inserted rows and refresh display
            LoadData();

            // Other accounts referenced via the Category column (or the Account column,
            // for category-matched rows) may have their own statement tab open showing
            // this transaction from the other side — refresh those too.
            var otherAccounts = toSave
                .Select(r => r.DisplayCategory)
                .Where(a => !string.IsNullOrWhiteSpace(a) && a != _account)
                .Distinct();
            if (Window.GetWindow(this) is MainWindow main)
                foreach (var acct in otherAccounts)
                    main.RefreshBankStatementTab(acct);
        }

        private void SaveRow(SqliteConnection conn, TransactionRowViewModel row)
        {
            var cleanMemo = CleanText(row.Memo);
            var cleanName = CleanText(row.Name);
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
            // For category-matched rows, DisplayCategory shows/edits the OTHER account
            // (the Account column); the Category column stays this account (_account).
            string dbCategory = row.MatchedViaCategory ? _account : row.DisplayCategory;
            string dbAccount  = row.MatchedViaCategory ? row.DisplayCategory : _account;

            if (row.IsNew)
            {
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, CheckNo, Name, Category, Clear, Amount, Memo, FinancialId, TrnType)
                    VALUES
                        (@acct,@date,@chk,@name,@cat,@clr,@amt,@memo,@fid,@trn);", conn);
                cmd.Parameters.AddWithValue("@acct", dbAccount);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@chk",  row.CheckNo ?? "");
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
                        Account=@acct, Date=@date, CheckNo=@chk, Name=@name, Category=@cat,
                        Clear=@clr, Amount=@amt, Memo=@memo, FinancialId=@fid, TrnType=@trn
                    WHERE rowid=@rid;", conn);
                cmd.Parameters.AddWithValue("@acct", dbAccount);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@chk",  row.CheckNo ?? "");
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

            // Update CatHistory keyed on Name (matches QfxImporter's NAME-based lookup)
            if (!row.MatchedViaCategory
                && !string.IsNullOrWhiteSpace(cleanName)
                && !string.IsNullOrWhiteSpace(row.DisplayCategory))
            {
                int? type = row.IsNew ? (hasPayment ? 1 : 2) : null;
                UpdateCatHistory(conn, cleanName, row.DisplayCategory, amount, type);
            }

            row.OriginalCategory = row.DisplayCategory;
            row.IsDirty = false;
            row.IsNew   = false;
        }

        private static void UpdateCatHistory(SqliteConnection conn, string name, string category, decimal amount, int? type = null)
        {
            using var chk = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description=@d;", conn);
            chk.Parameters.AddWithValue("@d", name);
            var existing = chk.ExecuteScalar() as string;

            if (existing is null)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO CatHistory(Description,Category,Amount,Type) VALUES(@d,@c,@a,@t);", conn);
                ins.Parameters.AddWithValue("@d", name);
                ins.Parameters.AddWithValue("@c", category);
                ins.Parameters.AddWithValue("@a", (double)amount);
                ins.Parameters.AddWithValue("@t", (object?)type ?? DBNull.Value);
                ins.ExecuteNonQuery();
            }
            else
            {
                if (existing != category)
                {
                    using var upd = new SqliteCommand(
                        "UPDATE CatHistory SET Category=@c WHERE Description=@d;", conn);
                    upd.Parameters.AddWithValue("@d", name);
                    upd.Parameters.AddWithValue("@c", category);
                    upd.ExecuteNonQuery();
                }

                // Keep the stored Amount current so autocomplete suggests the
                // most recently entered amount for this name, not whatever
                // amount happened to be used the very first time it was seen.
                if (amount != 0)
                {
                    using var updAmt = new SqliteCommand(
                        "UPDATE CatHistory SET Amount=@a WHERE Description=@d;", conn);
                    updAmt.Parameters.AddWithValue("@d", name);
                    updAmt.Parameters.AddWithValue("@a", (double)amount);
                    updAmt.ExecuteNonQuery();
                }

                if (type is not null)
                {
                    using var updType = new SqliteCommand(
                        "UPDATE CatHistory SET Type=@t WHERE Description=@d;", conn);
                    updType.Parameters.AddWithValue("@d", name);
                    updType.Parameters.AddWithValue("@t", type.Value);
                    updType.ExecuteNonQuery();
                }
            }
        }

        // Inserts/updates only the Category for a name, leaving Amount untouched
        // (or NULL on first insert) so it isn't locked to 0 before SaveRow runs.
        private static void UpdateCatHistoryCategoryOnly(SqliteConnection conn, string name, string category)
        {
            using var chk = new SqliteCommand(
                "SELECT Category FROM CatHistory WHERE Description=@d;", conn);
            chk.Parameters.AddWithValue("@d", name);
            var existing = chk.ExecuteScalar() as string;

            if (existing is null)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO CatHistory(Description,Category,Amount,Type) VALUES(@d,@c,NULL,NULL);", conn);
                ins.Parameters.AddWithValue("@d", name);
                ins.Parameters.AddWithValue("@c", category);
                ins.ExecuteNonQuery();
            }
            else if (existing != category)
            {
                using var upd = new SqliteCommand(
                    "UPDATE CatHistory SET Category=@c WHERE Description=@d;", conn);
                upd.Parameters.AddWithValue("@d", name);
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
            // and auto-fill Category and Payment/Deposit from the last known entry.
            if (e.Column == TrGrid.Columns[2]
                && e.Row.DataContext is TransactionRowViewModel newRow
                && newRow.IsNew)
            {
                Dispatcher.BeginInvoke(new Action(() => AutoFillFromCatHistory(newRow)));
            }

            // When the Category combo commits, sync the Category in CatHistory using
            // the row's Name. The Amount is intentionally NOT written here — at this
            // point Payment/Deposit may not have been entered yet, and writing Amount=0
            // here would overwrite/seed CatHistory before SaveRow has the real amount.
            // SaveRow (run on Save, after all columns have committed) is solely
            // responsible for persisting the Amount.
            if (e.Column == TrGrid.Columns[3]
                && e.Row.DataContext is TransactionRowViewModel row
                && !row.MatchedViaCategory)
            {
                var cleanName = CleanText(row.Name);
                var category  = row.DisplayCategory;
                if (!string.IsNullOrWhiteSpace(cleanName) && !string.IsNullOrWhiteSpace(category))
                {
                    using var conn = _db.OpenConnection();
                    UpdateCatHistoryCategoryOnly(conn, cleanName, category);
                }
            }

            // Recalculate balance after any edit (Payment/Deposit may have changed)
            Dispatcher.BeginInvoke(new Action(RecalcBalance));
        }

        private bool _suppressNameTextChanged;

        private void NameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.CaretIndex = tb.Text.Length;
            }
        }

        // Live prefix search as the user types in the Name column of a new row:
        // finds the first CatHistory Description starting with the typed text and
        // fills in the full Name, Category, and Payment/Deposit amount.
        //
        // Only triggers when the user is adding characters at the caret (typing
        // forward), never when deleting/editing — otherwise backspacing into an
        // already-accepted suggestion would immediately re-expand it, making the
        // suggested text effectively impossible to edit or shorten by hand.
        private void NameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressNameTextChanged) return;
            if (sender is not TextBox tb) return;
            if (tb.DataContext is not TransactionRowViewModel row || !row.IsNew) return;

            bool isInsertion = e.Changes.Any(c => c.AddedLength > 0 && c.RemovedLength == 0);
            if (!isInsertion) return;

            // Only auto-suggest while the caret is at the end of the text — once the
            // user is editing/positioning the cursor inside existing text, leave it alone.
            if (tb.CaretIndex != tb.Text.Length) return;

            var typed = CleanText(tb.Text);
            if (typed.Length < 2) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Description, Category, Amount, Type FROM CatHistory " +
                "WHERE Description LIKE @p ESCAPE '\\' ORDER BY Description LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@p", EscapeLike(typed) + "%");
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;

            var fullName = r.IsDBNull(0) ? "" : r.GetString(0);
            var cat      = r.IsDBNull(1) ? "" : r.GetString(1);
            var amt      = r.IsDBNull(2) ? 0m : (decimal)r.GetDouble(2);
            var type     = r.IsDBNull(3) ? 0  : r.GetInt32(3);

            if (string.IsNullOrEmpty(fullName) || fullName.Length <= typed.Length) return;

            row.NameMatchedCatHistory = true;

            _suppressNameTextChanged = true;
            var caret = tb.CaretIndex;
            tb.Text = fullName;
            tb.CaretIndex = caret;
            tb.SelectionStart = caret;
            tb.SelectionLength = fullName.Length - caret;
            _suppressNameTextChanged = false;

            if (!string.IsNullOrEmpty(cat))
                row.DisplayCategory = cat;

            if (amt > 0 && row.PaymentDecimal == 0 && row.DepositDecimal == 0)
            {
                if (type == 2)
                    row.Deposit = amt.ToString("N2");
                else
                    row.Payment = amt.ToString("N2");
            }
        }

        private static string EscapeLike(string s) =>
            s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        private void AutoFillFromCatHistory(TransactionRowViewModel row)
        {
            var name = CleanText(row.Name);
            if (string.IsNullOrEmpty(name)) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Category, Amount, Type FROM CatHistory WHERE Description = @d LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("@d", name);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                row.NameMatchedCatHistory = false;
                return;
            }

            row.NameMatchedCatHistory = true;

            var cat  = r.IsDBNull(0) ? "" : r.GetString(0);
            var amt  = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);
            var type = r.IsDBNull(2) ? 0  : r.GetInt32(2);

            if (!string.IsNullOrEmpty(cat))
                row.DisplayCategory = cat;

            if (amt > 0 && row.PaymentDecimal == 0 && row.DepositDecimal == 0)
            {
                if (type == 2)
                    row.Deposit = amt.ToString("N2");
                else
                    row.Payment = amt.ToString("N2");
            }
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

        // Selects the DataGridRow under the cursor before the context menu opens,
        // but preserves an existing multi-selection when right-clicking a row
        // that's already part of it (standard Windows Explorer behavior).
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
            TrGrid.Items.Refresh();
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

        // Reloads data from the database (called when the tab is re-activated).
        public void Refresh() => LoadData();

        // Called by the tab's close button before removing the tab
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
