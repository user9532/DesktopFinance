using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class LoanAccountDialog : Window
    {
        private readonly DatabaseManager _db;

        public LoanAccountDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
            DpStartDate.SelectedDate = DateTime.Today;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var name, out var date, out var institution,
                          out var loanAmount, out var numPayments, out var interestRate, out var escrow))
                return;

            if (_db.AccountExists(name))
            {
                MessageBox.Show($"Account '{name}' already exists in the Account Chart.",
                    "Account Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var conn = _db.OpenConnection();

            // Save to Transactions
            using (var cmd = new SqliteCommand(
                "INSERT INTO Transactions (Account, Date, Name, Memo, TrnType, Amount) " +
                "VALUES (@acct, @date, @name, 'Initial loan balance', 'CREDIT', @amt);", conn))
            {
                cmd.Parameters.AddWithValue("@acct", name);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@name", institution);
                cmd.Parameters.AddWithValue("@amt",  loanAmount);
                cmd.ExecuteNonQuery();
            }

            // Save to LoanData
            using (var cmd = new SqliteCommand(
                "INSERT INTO LoanData (AccountName, StartDate, Institution, Principle, Term, Interest, Escrow) " +
                "VALUES (@name, @date, @inst, @prin, @term, @rate, @escrow);", conn))
            {
                cmd.Parameters.AddWithValue("@name",   name);
                cmd.Parameters.AddWithValue("@date",   date);
                cmd.Parameters.AddWithValue("@inst",   institution);
                cmd.Parameters.AddWithValue("@prin",   loanAmount);
                cmd.Parameters.AddWithValue("@term",   numPayments);
                cmd.Parameters.AddWithValue("@rate",   interestRate);
                cmd.Parameters.AddWithValue("@escrow", escrow);
                cmd.ExecuteNonQuery();
            }

            // Save to AccountChart
            using (var cmd = new SqliteCommand(
                "INSERT INTO AccountChart (AccountName, AccountType, AccountGroup, AccountCode, Portfolio) " +
                "VALUES (@name, 'Loan', 'Liability', 3, '');", conn))
            {
                cmd.Parameters.AddWithValue("@name", name);
                cmd.ExecuteNonQuery();
            }

            DialogResult = true;
        }

        private bool Validate(out string name, out string date, out string institution,
                               out decimal loanAmount, out decimal numPayments,
                               out decimal interestRate, out decimal escrow)
        {
            name = institution = date = "";
            loanAmount = numPayments = interestRate = escrow = 0;

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
            if (!decimal.TryParse(TxtLoanAmount.Text, out loanAmount) || loanAmount <= 0)
            {
                ShowWarning("Amount of loan must be a positive number.");
                TxtLoanAmount.Focus();
                return false;
            }
            if (!decimal.TryParse(TxtNumPayments.Text, out numPayments) || numPayments <= 0)
            {
                ShowWarning("Total number of payments must be a positive number.");
                TxtNumPayments.Focus();
                return false;
            }
            if (!decimal.TryParse(TxtInterestRate.Text, out interestRate) || interestRate < 0)
            {
                ShowWarning("Interest rate must be a non-negative number.");
                TxtInterestRate.Focus();
                return false;
            }
            if (!decimal.TryParse(TxtEscrow.Text, out escrow) || escrow < 0)
            {
                ShowWarning("Escrow amount must be a non-negative number.");
                TxtEscrow.Focus();
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
