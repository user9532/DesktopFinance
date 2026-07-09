using FinanceApp.Data;
using FinanceApp.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public class LoanStatementRow
    {
        public string Date        { get; set; } = "";
        public string CheckNo     { get; set; } = "";
        public string Name        { get; set; } = "";
        public string Category    { get; set; } = "";
        public decimal Amount     { get; set; }
        public decimal Balance    { get; set; }
        public string Memo        { get; set; } = "";
        public string FinancialId { get; set; } = "";
    }

    public partial class LoanStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public LoanStatementView(DatabaseManager db, string account)
        {
            InitializeComponent();
            _db      = db;
            _account = account;
            LoadData();
        }

        private void LoadData()
        {
            var rows = new List<LoanStatementRow>();

            using var conn = _db.OpenConnection();

            // Rows where Account column matches — standard direction
            // DEBIT = positive amount, CREDIT = negative amount
            using (var cmd = new SqliteCommand(
                "SELECT Date, CheckNo, Name, Category, Amount, TrnType, Memo, FinancialId " +
                "FROM Transactions " +
                "WHERE Account = @acct " +
                "ORDER BY Date ASC;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var trnType = rdr.IsDBNull(5) ? "" : rdr.GetString(5);
                    var rawAmt  = rdr.IsDBNull(4) ? 0m : Convert.ToDecimal(rdr.GetDouble(4));
                    var signedAmt = trnType == "CREDIT" ? -rawAmt : rawAmt;

                    rows.Add(new LoanStatementRow
                    {
                        Date        = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        CheckNo     = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Name        = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                        Category    = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                        Amount      = signedAmt,
                        Memo        = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                        FinancialId = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
                    });
                }
            }

            // Rows where Category column matches — reversed direction and show Account in Category column
            // DEBIT = negative amount (treated as CREDIT), CREDIT = positive amount (treated as DEBIT)
            using (var cmd = new SqliteCommand(
                "SELECT Date, CheckNo, Name, Account, Amount, TrnType, Memo, FinancialId " +
                "FROM Transactions " +
                "WHERE Category = @acct AND Account != @acct " +
                "ORDER BY Date ASC;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", _account);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var trnType = rdr.IsDBNull(5) ? "" : rdr.GetString(5);
                    var rawAmt  = rdr.IsDBNull(4) ? 0m : Convert.ToDecimal(rdr.GetDouble(4));
                    // Reversed: DEBIT becomes CREDIT (negative), CREDIT becomes DEBIT (positive)
                    var signedAmt = trnType == "DEBIT" ? -rawAmt : rawAmt;

                    rows.Add(new LoanStatementRow
                    {
                        Date        = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        CheckNo     = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Name        = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                        Category    = rdr.IsDBNull(3) ? "" : rdr.GetString(3), // show Account value
                        Amount      = signedAmt,
                        Memo        = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                        FinancialId = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
                    });
                }
            }

            // Sort all rows by date ascending
            rows.Sort((a, b) => string.Compare(a.Date, b.Date, StringComparison.Ordinal));

            // Compute running balance
            decimal running = 0m;
            foreach (var row in rows)
            {
                running    += row.Amount;
                row.Balance = running;
            }

            LoanGrid.ItemsSource = rows;
        }

        private void Payment_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new LoanPaymentDialog(_db, _account) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                LoadData();
        }
    }
}
