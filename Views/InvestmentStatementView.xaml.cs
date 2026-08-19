using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FinanceApp.Views
{
    public partial class InvestmentStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        private readonly List<InvestmentRowViewModel> _allRows = new();
        private ObservableCollection<InvestmentRowViewModel> _view = new();

        public ObservableCollection<string> Categories { get; private set; } = new();

        private bool _hasChanges;

        public InvestmentStatementView(DatabaseManager db, string account)
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

        // ── data load ────────────────────────────────────────────────────

        private void LoadData()
        {
            _allRows.Clear();
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT rowid, Account, Date, CheckNo, Name, Category, Memo,
                       Action, Symbol, Quantity, Price, Commission, Amount, TrnType
                FROM   Transactions
                WHERE  Account = @acct OR Category = @acct
                ORDER  BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                var acctVal  = r.IsDBNull(1) ? "" : r.GetString(1);
                var category = r.IsDBNull(5) ? "" : r.GetString(5);
                var amount   = r.IsDBNull(12) ? 0m : (decimal)r.GetDouble(12);
                var trnType  = r.IsDBNull(13) ? "" : r.GetString(13);
                var viacat   = acctVal != _account;      // matched via Category column

                var effectiveTrn = viacat
                    ? (trnType == "DEBIT" ? "CREDIT" : "DEBIT")
                    : trnType;

                var row = new InvestmentRowViewModel();
                row.BeginLoad();

                row.RowId              = r.GetInt64(0);
                row.IsNew              = false;
                row.MatchedViaCategory = viacat;
                row.AccountValue       = acctVal;
                row.TrnType            = trnType;
                row.DateValue          = DateTime.TryParse(r.IsDBNull(2) ? "" : r.GetString(2), out var d) ? d : null;
                row.CheckNo            = r.IsDBNull(3) ? "" : r.GetString(3);
                row.Name                = r.IsDBNull(4) ? "" : r.GetString(4);
                row.Memo                = r.IsDBNull(6) ? "" : r.GetString(6);
                row.Action               = r.IsDBNull(7) ? "" : r.GetString(7);
                row.Symbol               = r.IsDBNull(8) ? "" : r.GetString(8);
                row.Quantity             = r.IsDBNull(9)  ? 0m : (decimal)r.GetDouble(9);
                row.Price                = r.IsDBNull(10) ? 0m : (decimal)r.GetDouble(10);
                row.Commission           = r.IsDBNull(11) ? 0m : (decimal)r.GetDouble(11);

                // CREDIT -> negative signed amount, DEBIT -> positive
                row.AmountText = (effectiveTrn == "CREDIT" ? -amount : amount).ToString("N2");

                row.DisplayCategory  = viacat ? acctVal : category;
                row.OriginalCategory = row.DisplayCategory;

                row.EndLoad();
                _allRows.Add(row);
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (TrGrid == null) return;

            _view = new ObservableCollection<InvestmentRowViewModel>(_allRows);
            RecalcBalance();
            TrGrid.ItemsSource = _view;
        }

        private void RecalcBalance()
        {
            decimal running = 0;
            foreach (var row in _view)
            {
                running += row.AmountDecimal;
                row.Balance = running;
            }
        }

        // ── add row ───────────────────────────────────────────────────────

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var row = new InvestmentRowViewModel
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

        private void SaveRow(SqliteConnection conn, InvestmentRowViewModel row)
        {
            var date   = row.DateValue?.ToString("yyyy-MM-dd") ?? "";
            var amount = row.AmountDecimal;

            string trnType;
            if (row.MatchedViaCategory)
                trnType = amount < 0 ? "DEBIT" : "CREDIT";
            else
                trnType = amount < 0 ? "CREDIT" : "DEBIT";

            string dbCategory = row.MatchedViaCategory ? _account : row.DisplayCategory;
            string dbAccount  = row.MatchedViaCategory ? row.DisplayCategory : _account;

            if (row.IsNew)
            {
                using var cmd = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, CheckNo, Name, Category, Memo, Action, Symbol,
                         Quantity, Price, Commission, Amount, TrnType)
                    VALUES
                        (@acct,@date,@chk,@name,@cat,@memo,@action,@sym,
                         @qty,@price,@comm,@amt,@trn);", conn);
                cmd.Parameters.AddWithValue("@acct",  dbAccount);
                cmd.Parameters.AddWithValue("@date",  date);
                cmd.Parameters.AddWithValue("@chk",   row.CheckNo ?? "");
                cmd.Parameters.AddWithValue("@name",  row.Name ?? "");
                cmd.Parameters.AddWithValue("@cat",   dbCategory ?? "");
                cmd.Parameters.AddWithValue("@memo",  row.Memo ?? "");
                cmd.Parameters.AddWithValue("@action",row.Action ?? "");
                cmd.Parameters.AddWithValue("@sym",   row.Symbol ?? "");
                cmd.Parameters.AddWithValue("@qty",   (double)row.Quantity);
                cmd.Parameters.AddWithValue("@price", (double)row.Price);
                cmd.Parameters.AddWithValue("@comm",  (double)row.Commission);
                cmd.Parameters.AddWithValue("@amt",   (double)Math.Abs(amount));
                cmd.Parameters.AddWithValue("@trn",   trnType);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new SqliteCommand(@"
                    UPDATE Transactions SET
                        Account=@acct, Date=@date, CheckNo=@chk, Name=@name, Category=@cat, Memo=@memo,
                        Action=@action, Symbol=@sym, Quantity=@qty, Price=@price,
                        Commission=@comm, Amount=@amt, TrnType=@trn
                    WHERE rowid=@rid;", conn);
                cmd.Parameters.AddWithValue("@acct",  dbAccount);
                cmd.Parameters.AddWithValue("@date",  date);
                cmd.Parameters.AddWithValue("@chk",   row.CheckNo ?? "");
                cmd.Parameters.AddWithValue("@name",  row.Name ?? "");
                cmd.Parameters.AddWithValue("@cat",   dbCategory ?? "");
                cmd.Parameters.AddWithValue("@memo",  row.Memo ?? "");
                cmd.Parameters.AddWithValue("@action",row.Action ?? "");
                cmd.Parameters.AddWithValue("@sym",   row.Symbol ?? "");
                cmd.Parameters.AddWithValue("@qty",   (double)row.Quantity);
                cmd.Parameters.AddWithValue("@price", (double)row.Price);
                cmd.Parameters.AddWithValue("@comm",  (double)row.Commission);
                cmd.Parameters.AddWithValue("@amt",   (double)Math.Abs(amount));
                cmd.Parameters.AddWithValue("@trn",   trnType);
                cmd.Parameters.AddWithValue("@rid",   row.RowId);
                cmd.ExecuteNonQuery();
            }

            row.OriginalCategory = row.DisplayCategory;
            row.IsDirty = false;
            row.IsNew   = false;
        }

        // ── grid events ───────────────────────────────────────────────────

        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            _hasChanges = true;
            Dispatcher.BeginInvoke(new Action(RecalcBalance));
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
            if (row?.DataContext is InvestmentRowViewModel vm)
                TrGrid.SelectedItem = vm;
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (TrGrid.SelectedItem is not InvestmentRowViewModel row) return;

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

        // ── toolbar buttons (existing quick-entry dialogs) ─────────────────

        private void Transaction_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.InvestmentTransactionDialog(_db, _account)
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
            LoadData();
        }

        private void Trade_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.InvestmentTradeDialog(_db, _account)
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
            LoadData();
        }

        private void Portfolio_Click(object sender, RoutedEventArgs e)
        {
            string portfolio = "";
            using (var conn = _db.OpenConnection())
            using (var cmd = new SqliteCommand(
                "SELECT Portfolio FROM AccountChart WHERE AccountName = @acct LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                portfolio = cmd.ExecuteScalar() as string ?? "";
            }

            if (string.IsNullOrEmpty(portfolio))
            {
                MessageBox.Show(
                    "No portfolio is associated with this investment account.",
                    "Portfolio", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (Window.GetWindow(this) is MainWindow main)
                main.ActivateOrOpenPortfolio(portfolio);
        }
    }

    public class InvestmentRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public long   RowId              { get; set; }
        public bool   IsNew              { get; set; }
        public bool   MatchedViaCategory { get; set; }
        public string AccountValue       { get; set; } = "";
        public string OriginalCategory   { get; set; } = "";
        public bool   IsDirty            { get; set; }
        public string TrnType            { get; set; } = "";

        private bool _loading;
        public void BeginLoad() => _loading = true;
        public void EndLoad()   => _loading = false;
        private void Touch() { if (!_loading) IsDirty = true; }

        private DateTime? _dateValue;
        public DateTime? DateValue
        {
            get => _dateValue;
            set { _dateValue = value; Notify(); Notify(nameof(DateDisplay)); Touch(); }
        }
        public string DateDisplay => _dateValue?.ToString("yyyy-MM-dd") ?? "";

        private string _checkNo = "";
        public string CheckNo
        {
            get => _checkNo;
            set { _checkNo = value; Notify(); Touch(); }
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; Notify(); Touch(); }
        }

        private string _displayCategory = "";
        public string DisplayCategory
        {
            get => _displayCategory;
            set { _displayCategory = value; Notify(); Touch(); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { _memo = value; Notify(); Touch(); }
        }

        private string _action = "";
        public string Action
        {
            get => _action;
            set { _action = value; Notify(); Touch(); }
        }

        private string _symbol = "";
        public string Symbol
        {
            get => _symbol;
            set { _symbol = value; Notify(); Touch(); }
        }

        private decimal _quantity;
        public decimal Quantity
        {
            get => _quantity;
            set { _quantity = value; Notify(); Touch(); }
        }

        private decimal _price;
        public decimal Price
        {
            get => _price;
            set { _price = value; Notify(); Touch(); }
        }

        private decimal _commission;
        public decimal Commission
        {
            get => _commission;
            set { _commission = value; Notify(); Touch(); }
        }

        private string _amountText = "";
        public string AmountText
        {
            get => _amountText;
            set { _amountText = value; Notify(); Touch(); }
        }

        public decimal AmountDecimal =>
            decimal.TryParse(_amountText, out var d) ? d : 0m;

        private decimal _balance;
        public decimal Balance
        {
            get => _balance;
            set { _balance = value; Notify(); }
        }
    }
}
