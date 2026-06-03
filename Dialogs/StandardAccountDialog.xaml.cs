using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class StandardAccountDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly NewAccountType  _accountType;

        public StandardAccountDialog(DatabaseManager db, NewAccountType accountType)
        {
            InitializeComponent();
            _db          = db;
            _accountType = accountType;

            LblHeading.Text = accountType switch
            {
                NewAccountType.CreditCard => "Credit Card Account",
                NewAccountType.Investment => "Investment Account",
                _                         => "Bank Account"
            };

            DpStartDate.SelectedDate = DateTime.Today;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var name, out var date, out var institution, out var balance))
                return;

            if (_db.AccountExists(name))
            {
                MessageBox.Show($"Account '{name}' already exists in the Account Chart.",
                    "Account Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string trnType = _accountType == NewAccountType.CreditCard ? "CREDIT" : "DEBIT";

            using var conn = _db.OpenConnection();

            // Save to Transactions
            using (var cmd = new SqliteCommand(
                "INSERT INTO Transactions (Account, Date, Name, Memo, Amount, TrnType) " +
                "VALUES (@acct, @date, @name, 'Beginning balance', @amt, @trntype);", conn))
            {
                cmd.Parameters.AddWithValue("@acct",    name);
                cmd.Parameters.AddWithValue("@date",    date);
                cmd.Parameters.AddWithValue("@name",    institution);
                cmd.Parameters.AddWithValue("@amt",     balance);
                cmd.Parameters.AddWithValue("@trntype", trnType);
                cmd.ExecuteNonQuery();
            }

            // Determine AccountChart values per account type
            var (acctType, acctGroup, acctCode, portfolio) = _accountType switch
            {
                NewAccountType.CreditCard => ("Credit Card", "Liability", 1, ""),
                NewAccountType.Investment => ("Investment",  "Asset",     2, name + "-Portfolio"),
                _                         => ("Bank",        "Asset",     1, "")
            };

            // Save to AccountChart
            using (var cmd = new SqliteCommand(
                "INSERT INTO AccountChart (AccountName, AccountType, AccountGroup, AccountCode, Portfolio) " +
                "VALUES (@name, @type, @grp, @code, @port);", conn))
            {
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@type", acctType);
                cmd.Parameters.AddWithValue("@grp",  acctGroup);
                cmd.Parameters.AddWithValue("@code", acctCode);
                cmd.Parameters.AddWithValue("@port", portfolio);
                cmd.ExecuteNonQuery();
            }

            DialogResult = true;
        }

        private bool Validate(out string name, out string date, out string institution, out decimal balance)
        {
            name = institution = date = "";
            balance = 0;

            if (string.IsNullOrWhiteSpace(TxtAccountName.Text))
            {
                ShowWarning("Account name is required.");
                TxtAccountName.Focus();
                return false;
            }
            if (DpStartDate.SelectedDate is null)
            {
                ShowWarning("Account start date is required.");
                DpStartDate.Focus();
                return false;
            }
            if (!decimal.TryParse(TxtBalance.Text, out balance))
            {
                ShowWarning("Starting account balance must be a valid number.");
                TxtBalance.Focus();
                return false;
            }

            name        = TxtAccountName.Text.Trim();
            date        = DpStartDate.SelectedDate!.Value.ToString("yyyy-MM-dd");
            institution = TxtInstitution.Text.Trim();
            return true;
        }

        private static void ShowWarning(string message) =>
            MessageBox.Show(message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
