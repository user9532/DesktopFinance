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
            DpStartDate.SelectedDate  = DateTime.Today;
            DpFirstPmnt.SelectedDate  = DateTime.Today;
            LoadProceedsToOptions();
        }

        private void LoadProceedsToOptions()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE AccountGroup IN ('Asset','Liability') ORDER BY AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read()) CbProceedsTo.Items.Add(r.GetString(0));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out var name, out var date, out var firstPmnt, out var institution,
                          out var loanAmount, out var numPayments, out var interestRate, out var escrow,
                          out var proceedsTo))
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
                "INSERT INTO Transactions (Account, Date, Name, Memo, TrnType, Amount, Category) " +
                "VALUES (@acct, @date, @name, 'Initial loan balance', 'CREDIT', @amt, @cat);", conn))
            {
                cmd.Parameters.AddWithValue("@acct", name);
                cmd.Parameters.AddWithValue("@date", date);
                cmd.Parameters.AddWithValue("@name", institution);
                cmd.Parameters.AddWithValue("@amt",  loanAmount);
                cmd.Parameters.AddWithValue("@cat",  proceedsTo);
                cmd.ExecuteNonQuery();
            }

            // Calculate monthly payment using standard amortization formula
            double monthlyRate = (double)interestRate / 1200.0;
            double n           = (double)numPayments;
            double pmt         = monthlyRate == 0
                ? (double)loanAmount / n
                : (double)loanAmount * monthlyRate / (1.0 - Math.Pow(1.0 + monthlyRate, -n));

            // Round up to the nearest cent so the loan is never under-paid.
            pmt = Math.Ceiling(pmt * 100.0) / 100.0;

            // Save to LoanData
            using (var cmd = new SqliteCommand(
                "INSERT INTO LoanData (AccountName, StartDate, FirstPmnt, Institution, Principal, Term, Interest, Escrow, Pmt, CurrBalance) " +
                "VALUES (@name, @date, @firstPmnt, @inst, @prin, @term, @rate, @escrow, @pmt, @currBalance);", conn))
            {
                cmd.Parameters.AddWithValue("@name",        name);
                cmd.Parameters.AddWithValue("@date",        date);
                cmd.Parameters.AddWithValue("@firstPmnt",   firstPmnt);
                cmd.Parameters.AddWithValue("@inst",        institution);
                cmd.Parameters.AddWithValue("@prin",        loanAmount);
                cmd.Parameters.AddWithValue("@term",        numPayments);
                cmd.Parameters.AddWithValue("@rate",        interestRate);
                cmd.Parameters.AddWithValue("@escrow",      escrow);
                cmd.Parameters.AddWithValue("@pmt",         pmt);
                cmd.Parameters.AddWithValue("@currBalance", loanAmount);
                cmd.ExecuteNonQuery();
            }

            // Save to RecurTrans
            long nextRecurId;
            using (var idCmd = new SqliteCommand(
                "SELECT COALESCE(MAX(Id), 0) + 1 FROM RecurTrans;", conn))
            {
                nextRecurId = (long)idCmd.ExecuteScalar()!;
            }
            using (var cmd = new SqliteCommand(
                "INSERT INTO RecurTrans (Id, Name, Amount, Memo, Account, Category, Frequency, NextDue, LastPosted, TrType) " +
                "VALUES (@id, @name, @amt, @memo, @acct, '', 30, @nextDue, '', 3);", conn))
            {
                cmd.Parameters.AddWithValue("@id",       nextRecurId);
                cmd.Parameters.AddWithValue("@name",     name);
                cmd.Parameters.AddWithValue("@amt",      pmt + (double)escrow);
                cmd.Parameters.AddWithValue("@memo",     institution);
                cmd.Parameters.AddWithValue("@acct",     name);
                cmd.Parameters.AddWithValue("@nextDue",  firstPmnt);
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

        private bool Validate(out string name, out string date, out string firstPmnt,
                               out string institution,
                               out decimal loanAmount, out decimal numPayments,
                               out decimal interestRate, out decimal escrow,
                               out string proceedsTo)
        {
            name = institution = date = firstPmnt = proceedsTo = "";
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
            if (DpFirstPmnt.SelectedDate is null)
            {
                ShowWarning("First payment date is required.");
                DpFirstPmnt.Focus();
                return false;
            }
            if (DpFirstPmnt.SelectedDate.Value <= DpStartDate.SelectedDate.Value)
            {
                ShowWarning("First payment date must be later than the account start date.");
                DpFirstPmnt.Focus();
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
            if (CbProceedsTo.SelectedItem is not string)
            {
                ShowWarning("Proceeds go to is required.");
                CbProceedsTo.Focus();
                return false;
            }

            name        = TxtAccountName.Text.Trim();
            proceedsTo  = (string)CbProceedsTo.SelectedItem!;
            date        = DpStartDate.SelectedDate!.Value.ToString("yyyy-MM-dd");
            firstPmnt   = DpFirstPmnt.SelectedDate!.Value.ToString("yyyy-MM-dd");
            institution = TxtInstitution.Text.Trim();
            return true;
        }

        private static void ShowWarning(string message) =>
            MessageBox.Show(message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
