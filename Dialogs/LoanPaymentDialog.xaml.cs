using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class LoanPaymentDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        private string _institution = "";
        private double _pmt;
        private double _interestRate; // annual %
        private double _escrowAmt;
        private double _currBalance;
        private string _firstPmnt = "";
        private string _startDate = "";
        private string _lastPosted = "";
        private string _nextDue = "";
        private string _recurCategory = "";

        public LoanPaymentDialog(DatabaseManager db, string account)
        {
            InitializeComponent();
            _db      = db;
            _account = account;

            LoadAccounts();
            LoadCategories();
            LoadLoanData();

            TxtLoanAccount.Text = _account;
            TxtDescription.Text = _institution;
            TxtEscrow.Text      = _escrowAmt.ToString("N2");

            if (!string.IsNullOrEmpty(_recurCategory))
                CbInterestCategory.SelectedItem = _recurCategory;

            DpPaymentDate.SelectedDate = DateTime.TryParse(_nextDue, out var defaultDate) ? defaultDate : DateTime.Today;

            Recalculate();
        }

        private void LoadAccounts()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountType = 'Bank' " +
                "ORDER BY AccountName;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                CbFromAccount.Items.Add(rdr.GetString(0));

            if (CbFromAccount.Items.Count > 0)
                CbFromAccount.SelectedIndex = 0;
        }

        private void LoadCategories()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "ORDER BY CASE AccountType " +
                "    WHEN 'Expense'   THEN 1 " +
                "    WHEN 'Income'    THEN 2 " +
                "    WHEN 'Asset'     THEN 3 " +
                "    WHEN 'Liability' THEN 4 " +
                "    ELSE 5 END, AccountName;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var name = rdr.GetString(0);
                CbInterestCategory.Items.Add(name);
                CbEscrowCategory.Items.Add(name);
            }
        }

        private void LoadLoanData()
        {
            using var conn = _db.OpenConnection();
            using (var cmd = new SqliteCommand(
                "SELECT Institution, Pmt, Interest, Escrow, CurrBalance, FirstPmnt, StartDate " +
                "FROM LoanData WHERE AccountName = @acct;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    _institution  = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                    _pmt          = rdr.IsDBNull(1) ? 0 : rdr.GetDouble(1);
                    _interestRate = rdr.IsDBNull(2) ? 0 : rdr.GetDouble(2);
                    _escrowAmt    = rdr.IsDBNull(3) ? 0 : rdr.GetDouble(3);
                    _currBalance  = rdr.IsDBNull(4) ? 0 : rdr.GetDouble(4);
                    _firstPmnt    = rdr.IsDBNull(5) ? "" : rdr.GetString(5);
                    _startDate    = rdr.IsDBNull(6) ? "" : rdr.GetString(6);
                }
            }

            using (var cmd = new SqliteCommand(
                "SELECT LastPosted, NextDue, Category FROM RecurTrans WHERE Account = @acct AND TrType = 3;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    _lastPosted    = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                    _nextDue       = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                    _recurCategory = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
                }
            }
        }

        private void DpPaymentDate_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Recalculate();
        }

        private void Recalculate()
        {
            if (DpPaymentDate.SelectedDate is not DateTime payDate)
                return;

            string sinceStr = string.IsNullOrWhiteSpace(_lastPosted) ? _startDate : _lastPosted;
            if (!DateTime.TryParse(sinceStr, out var sinceDate))
                sinceDate = payDate;

            int days = (payDate - sinceDate).Days;
            if (days < 0) days = 0;

            double interest  = _currBalance * (_interestRate / 100.0) * (days / 365.0);
            double principal = _pmt - interest;

            TxtInterest.Text  = interest.ToString("N2");
            TxtPrincipal.Text = principal.ToString("N2");

            UpdateTotal();
        }

        private void UpdateTotal()
        {
            double.TryParse(TxtPrincipal.Text, out var principal);
            double.TryParse(TxtInterest.Text, out var interest);
            double.TryParse(TxtEscrow.Text, out var escrow);
            TxtTotal.Text = (principal + interest + escrow).ToString("N2");
        }

        private void Post_Click(object sender, RoutedEventArgs e)
        {
            if (CbFromAccount.SelectedItem is not string fromAccount || fromAccount.Length == 0)
            {
                Warn("Please select a From Account.");
                return;
            }
            if (DpPaymentDate.SelectedDate is not DateTime payDate)
            {
                Warn("Please select a Payment Date.");
                return;
            }
            if (!double.TryParse(TxtPrincipal.Text, out var principal))
            {
                Warn("Loan Principal must be a number.");
                return;
            }
            if (!double.TryParse(TxtInterest.Text, out var interest))
            {
                Warn("Interest must be a number.");
                return;
            }
            if (CbInterestCategory.SelectedItem is not string interestCategory || interestCategory.Length == 0)
            {
                Warn("Please select an Interest Category.");
                return;
            }
            if (!double.TryParse(TxtEscrow.Text, out var escrow))
            {
                Warn("Escrow must be a number.");
                return;
            }
            string? escrowCategory = CbEscrowCategory.SelectedItem as string;
            if (escrow > 0 && string.IsNullOrEmpty(escrowCategory))
            {
                Warn("Please select an Escrow Category.");
                return;
            }

            string dateStr = payDate.ToString("yyyy-MM-dd");
            string checkNo = TxtCheckNo.Text ?? "";

            using var conn = _db.OpenConnection();

            using (var cmd = new SqliteCommand(
                "INSERT INTO Transactions (Account, Date, CheckNo, Name, Amount, Memo, TrnType, Category) " +
                "VALUES (@acct, @date, @chk, @name, @amt, @memo, 'CREDIT', @cat);", conn))
            {
                cmd.Parameters.AddWithValue("@acct", fromAccount);
                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@chk",  checkNo);
                cmd.Parameters.AddWithValue("@name", $"Principal payment {_account}");
                cmd.Parameters.AddWithValue("@amt",  principal);
                cmd.Parameters.AddWithValue("@memo", _institution);
                cmd.Parameters.AddWithValue("@cat",  _account);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(
                "INSERT INTO Transactions (Account, Date, CheckNo, Name, Amount, Memo, TrnType, Source, Category) " +
                "VALUES (@acct, @date, @chk, @name, @amt, @memo, 'CREDIT', @src, @cat);", conn))
            {
                cmd.Parameters.AddWithValue("@acct", fromAccount);
                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@chk",  checkNo);
                cmd.Parameters.AddWithValue("@name", $"Interest {_account}");
                cmd.Parameters.AddWithValue("@amt",  interest);
                cmd.Parameters.AddWithValue("@memo", _institution);
                cmd.Parameters.AddWithValue("@src",  _account);
                cmd.Parameters.AddWithValue("@cat",  interestCategory);
                cmd.ExecuteNonQuery();
            }

            if (escrow > 0)
            {
                using var cmd = new SqliteCommand(
                    "INSERT INTO Transactions (Account, Date, CheckNo, Name, Amount, Memo, TrnType, Category) " +
                    "VALUES (@acct, @date, @chk, @name, @amt, @memo, 'CREDIT', @cat);", conn);
                cmd.Parameters.AddWithValue("@acct", fromAccount);
                cmd.Parameters.AddWithValue("@date", dateStr);
                cmd.Parameters.AddWithValue("@chk",  checkNo);
                cmd.Parameters.AddWithValue("@name", $"Escrow {_account}");
                cmd.Parameters.AddWithValue("@amt",  escrow);
                cmd.Parameters.AddWithValue("@memo", _institution);
                cmd.Parameters.AddWithValue("@cat",  escrowCategory);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(
                "SELECT Frequency FROM RecurTrans WHERE Account = @acct AND TrType = 3;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                var result = cmd.ExecuteScalar();
                int frequency = (result == null || result == DBNull.Value) ? 30 : Convert.ToInt32(result);
                string nextDueStr = payDate.AddDays(frequency).ToString("yyyy-MM-dd");

                using var updCmd = new SqliteCommand(
                    "UPDATE RecurTrans SET LastPosted = @lastPosted, NextDue = @nextDue, Category = @cat " +
                    "WHERE Account = @acct AND TrType = 3;", conn);
                updCmd.Parameters.AddWithValue("@lastPosted", dateStr);
                updCmd.Parameters.AddWithValue("@nextDue",    nextDueStr);
                updCmd.Parameters.AddWithValue("@cat",        interestCategory);
                updCmd.Parameters.AddWithValue("@acct",       _account);
                updCmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(
                "UPDATE LoanData SET CurrBalance = @newBalance WHERE AccountName = @acct;", conn))
            {
                cmd.Parameters.AddWithValue("@newBalance", _currBalance - principal);
                cmd.Parameters.AddWithValue("@acct",       _account);
                cmd.ExecuteNonQuery();
            }

            DialogResult = true;
        }

        private static void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
