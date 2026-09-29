using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class InvestmentTransactionDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public InvestmentTransactionDialog(DatabaseManager db, string account)
        {
            InitializeComponent();
            _db      = db;
            _account = account;
            LoadCategories();
            CbType.SelectedIndex = 0;
        }

        private void LoadCategories()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT AccountName FROM AccountChart
                WHERE  AccountCode = 1
                ORDER  BY
                    CASE AccountGroup
                        WHEN 'Asset'     THEN 1
                        WHEN 'Liability' THEN 2
                        WHEN 'Expense'   THEN 3
                        WHEN 'Income'    THEN 4
                        ELSE 5
                    END,
                    AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbCategory.Items.Add(r.GetString(0));
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
            if (CbCategory.SelectedItem == null)
            {
                MessageBox.Show("Please select a category.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CbCategory.Focus();
                return;
            }
            if (CbType.SelectedItem == null)
            {
                MessageBox.Show("Please select a type.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CbType.Focus();
                return;
            }
            if (!decimal.TryParse(TxtAmount.Text.Trim(), out var amount))
            {
                MessageBox.Show("Please enter a valid numeric amount.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAmount.Focus();
                return;
            }

            var typeLabel = ((ComboBoxItem)CbType.SelectedItem).Content.ToString();
            var trnType   = typeLabel == "Payment" ? "CREDIT" : "DEBIT";

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                INSERT INTO Transactions
                    (Account, Date, CheckNo, Name, Category, TrnType, Amount, Memo)
                VALUES
                    (@acct, @date, @chk, @name, @cat, @trn, @amt, @memo);", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            cmd.Parameters.AddWithValue("@date", DpDate.SelectedDate.Value.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@chk",  TxtCheckNo.Text.Trim());
            cmd.Parameters.AddWithValue("@name", TxtName.Text.Trim());
            cmd.Parameters.AddWithValue("@cat",  (string)CbCategory.SelectedItem);
            cmd.Parameters.AddWithValue("@trn",  trnType);
            cmd.Parameters.AddWithValue("@amt",  (double)amount);
            cmd.Parameters.AddWithValue("@memo", TxtMemo.Text.Trim());
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
