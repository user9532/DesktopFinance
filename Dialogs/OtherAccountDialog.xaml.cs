using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class OtherAccountDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly NewAccountType  _accountType;

        public OtherAccountDialog(DatabaseManager db, NewAccountType accountType)
        {
            InitializeComponent();
            _db          = db;
            _accountType = accountType;

            LblHeading.Text = accountType == NewAccountType.OtherLiability
                ? "Other Liability Account"
                : "Other Asset Account";

            DpStartDate.SelectedDate = DateTime.Today;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var name, out var date, out var balance))
                return;

            if (_db.AccountExists(name))
            {
                MessageBox.Show($"Account '{name}' already exists in the Account Chart.",
                    "Account Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Always store as positive amount
            var absBalance = Math.Abs(balance);
            string trnType = _accountType == NewAccountType.OtherLiability ? "CREDIT" : "DEBIT";

            using var conn = _db.OpenConnection();

            // Account name goes in the Name column (no institution field for this type)
            using var cmd = new SqliteCommand(
                "INSERT INTO Transactions (Account, Date, Name, Memo, Amount, TrnType) " +
                "VALUES (@acct, @date, @name, 'Beginning balance', @amt, @trntype);", conn);

            cmd.Parameters.AddWithValue("@acct",    name);
            cmd.Parameters.AddWithValue("@date",    date);
            cmd.Parameters.AddWithValue("@name",    name);
            cmd.Parameters.AddWithValue("@amt",     absBalance);
            cmd.Parameters.AddWithValue("@trntype", trnType);
            cmd.ExecuteNonQuery();

            // Save to AccountChart
            var (acctType, acctGroup) = _accountType == NewAccountType.OtherLiability
                ? ("Other Liability", "Liability")
                : ("Other Asset",     "Asset");

            using (var chartCmd = new SqliteCommand(
                "INSERT INTO AccountChart (AccountName, AccountType, AccountGroup, AccountCode, Portfolio) " +
                "VALUES (@name, @type, @grp, 1, '');", conn))
            {
                chartCmd.Parameters.AddWithValue("@name", name);
                chartCmd.Parameters.AddWithValue("@type", acctType);
                chartCmd.Parameters.AddWithValue("@grp",  acctGroup);
                chartCmd.ExecuteNonQuery();
            }

            DialogResult = true;
        }

        private bool Validate(out string name, out string date, out decimal balance)
        {
            name = date = "";
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

            name = TxtAccountName.Text.Trim();
            date = DpStartDate.SelectedDate!.Value.ToString("yyyy-MM-dd");
            return true;
        }

        private static void ShowWarning(string message) =>
            MessageBox.Show(message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
