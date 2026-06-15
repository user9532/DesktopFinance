using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class InvestmentTradeDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public InvestmentTradeDialog(DatabaseManager db, string account)
        {
            InitializeComponent();
            _db      = db;
            _account = account;
            LoadSymbols();
            CbAction.SelectedIndex = 0;
        }

        private void LoadSymbols()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Symbol FROM Securities ORDER BY Symbol;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbSymbol.Items.Add(r.GetString(0));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DpDate.SelectedDate == null)
            {
                MessageBox.Show("Please select a date.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DpDate.Focus();
                return;
            }
            if (CbAction.SelectedItem == null)
            {
                MessageBox.Show("Please select an action.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CbAction.Focus();
                return;
            }
            if (CbSymbol.SelectedItem == null)
            {
                MessageBox.Show("Please select a security.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CbSymbol.Focus();
                return;
            }
            if (!decimal.TryParse(TxtQuantity.Text.Trim(), out var quantity) || quantity <= 0)
            {
                MessageBox.Show("Please enter a valid quantity.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtQuantity.Focus();
                return;
            }
            if (!decimal.TryParse(TxtPrice.Text.Trim(), out var price) || price <= 0)
            {
                MessageBox.Show("Please enter a valid price.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPrice.Focus();
                return;
            }

            decimal commission = 0;
            var commissionText = TxtCommission.Text.Trim();
            if (!string.IsNullOrEmpty(commissionText) &&
                !decimal.TryParse(commissionText, out commission))
            {
                MessageBox.Show("Please enter a valid commission amount.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCommission.Focus();
                return;
            }

            var action = ((ComboBoxItem)CbAction.SelectedItem).Content.ToString()!;
            var symbol = (string)CbSymbol.SelectedItem;
            var date   = DpDate.SelectedDate.Value.ToString("yyyy-MM-dd");

            // Look up Description and Portfolio
            string description = "";
            string portfolio   = "";
            using (var conn = _db.OpenConnection())
            {
                using (var cmd = new SqliteCommand(
                    "SELECT Description FROM Securities WHERE Symbol = @sym;", conn))
                {
                    cmd.Parameters.AddWithValue("@sym", symbol);
                    description = cmd.ExecuteScalar() as string ?? "";
                }
                using (var cmd = new SqliteCommand(
                    "SELECT Portfolio FROM AccountChart WHERE AccountName = @acct;", conn))
                {
                    cmd.Parameters.AddWithValue("@acct", _account);
                    portfolio = cmd.ExecuteScalar() as string ?? "";
                }
            }

            var amount  = quantity * price + commission;
            var trnType = action == "Buy" ? "CREDIT" : "DEBIT";
            var memo    = $"{action} {quantity:G29} {symbol} at ${price:G29}";

            using var conn2 = _db.OpenConnection();
            using var ins = new SqliteCommand(@"
                INSERT INTO Transactions
                    (Account, Date, Action, Symbol, Quantity, Price, Commission,
                     Amount, Name, Memo, Category, TrnType)
                VALUES
                    (@acct, @date, @action, @sym, @qty, @price, @comm,
                     @amt, @name, @memo, @cat, @trn);", conn2);
            ins.Parameters.AddWithValue("@acct",   _account);
            ins.Parameters.AddWithValue("@date",   date);
            ins.Parameters.AddWithValue("@action", action);
            ins.Parameters.AddWithValue("@sym",    symbol);
            ins.Parameters.AddWithValue("@qty",    (double)quantity);
            ins.Parameters.AddWithValue("@price",  (double)price);
            ins.Parameters.AddWithValue("@comm",   (double)commission);
            ins.Parameters.AddWithValue("@amt",    (double)amount);
            ins.Parameters.AddWithValue("@name",   description);
            ins.Parameters.AddWithValue("@memo",   memo);
            ins.Parameters.AddWithValue("@cat",    portfolio);
            ins.Parameters.AddWithValue("@trn",    trnType);
            ins.ExecuteNonQuery();

            // Clear for another entry
            DpDate.SelectedDate  = null;
            CbAction.SelectedIndex = 0;
            CbSymbol.SelectedItem  = null;
            TxtQuantity.Text       = "";
            TxtPrice.Text          = "";
            TxtCommission.Text     = "";

            MessageBox.Show("Trade saved.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
