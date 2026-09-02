using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class LoanInfoDialog : Window
    {
        public LoanInfoDialog(DatabaseManager db, string account)
        {
            InitializeComponent();
            LoadData(db, account);
        }

        private void LoadData(DatabaseManager db, string account)
        {
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName, Institution, StartDate, Principal, Interest, Term, " +
                "Escrow, Pmt, FirstPmnt, CurrBalance FROM LoanData WHERE AccountName = @acct;", conn);
            cmd.Parameters.AddWithValue("@acct", account);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;

            TxtAccountName.Text  = r.IsDBNull(0) ? "" : r.GetString(0);
            TxtInstitution.Text  = r.IsDBNull(1) ? "" : r.GetString(1);
            TxtStartDate.Text    = FormatDate(r.IsDBNull(2) ? "" : r.GetString(2));
            TxtPrincipal.Text    = (r.IsDBNull(3) ? 0m : Convert.ToDecimal(r.GetDouble(3))).ToString("N2");
            TxtInterest.Text     = (r.IsDBNull(4) ? 0m : Convert.ToDecimal(r.GetDouble(4))).ToString("N2") + "%";
            TxtTerm.Text         = (r.IsDBNull(5) ? 0m : Convert.ToDecimal(r.GetDouble(5))).ToString("N0");
            TxtEscrow.Text       = (r.IsDBNull(6) ? 0m : Convert.ToDecimal(r.GetDouble(6))).ToString("N2");
            TxtPmt.Text          = (r.IsDBNull(7) ? 0m : Convert.ToDecimal(r.GetDouble(7))).ToString("N2");
            TxtFirstPmnt.Text    = FormatDate(r.IsDBNull(8) ? "" : r.GetString(8));
            TxtCurrBalance.Text  = (r.IsDBNull(9) ? 0m : Convert.ToDecimal(r.GetDouble(9))).ToString("N2");
        }

        private static string FormatDate(string s) =>
            DateTime.TryParse(s, out var d) ? d.ToString("MM/dd/yyyy") : s;

        private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
