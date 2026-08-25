using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class CashFlowView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly DateTime _start;
        private readonly DateTime _end;
        private List<NetWorthRow> _rows = new();

        public CashFlowView(DatabaseManager db, DateTime start, DateTime end)
        {
            _db    = db;
            _start = start;
            _end   = end;
            InitializeComponent();
            TxtPeriod.Text = $"For the Period Ended {_start:yyyy-MM-dd} - {_end:yyyy-MM-dd}";
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var rows = new List<NetWorthRow>();

            string startStr  = _start.ToString("yyyy-MM-dd");
            string endStr    = _end.ToString("yyyy-MM-dd");
            string dayBefore = _start.AddDays(-1).ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();

            var accounts = LoadNonBankAccounts(conn);

            // ── Cash Inflows: Account-side CREDIT + Category-side DEBIT ──
            rows.Add(new NetWorthRow { Label = "Cash Inflows", IsBold = true });

            decimal totalInflows = 0;
            foreach (var (name, portfolio) in accounts)
            {
                var amt = SumFlow(conn, name, portfolio, startStr, endStr,
                                  accountSideTrn: "CREDIT", categorySideTrn: "DEBIT");
                totalInflows += amt;
                if (amt != 0)
                    rows.Add(new NetWorthRow { Label = name, Amount = amt, IsIndented = true });
            }
            rows.Add(new NetWorthRow { Label = "Total Cash Inflows", Amount = totalInflows, IsBold = true });

            // ── Cash Outflows: Account-side DEBIT + Category-side CREDIT ──
            rows.Add(new NetWorthRow { Label = "Cash Outflows", IsBold = true });

            decimal totalOutflows = 0;
            foreach (var (name, portfolio) in accounts)
            {
                var amt = SumFlow(conn, name, portfolio, startStr, endStr,
                                  accountSideTrn: "DEBIT", categorySideTrn: "CREDIT");
                totalOutflows += amt;
                if (amt != 0)
                    rows.Add(new NetWorthRow { Label = name, Amount = amt, IsIndented = true });
            }
            rows.Add(new NetWorthRow { Label = "Total Cash Outflows", Amount = totalOutflows, IsBold = true });

            // ── Net / beginning / ending cash ──
            rows.Add(new NetWorthRow
            {
                Label  = "Net Cashflows",
                Amount = totalInflows - totalOutflows,
                IsBold = true
            });
            rows.Add(new NetWorthRow
            {
                Label  = "Cash and bank – beginning",
                Amount = BankBalance(conn, dayBefore),
                IsBold = true
            });
            rows.Add(new NetWorthRow
            {
                Label  = "Cash and bank – ending",
                Amount = BankBalance(conn, endStr),
                IsBold = true
            });

            _rows = rows;
            RowsHost.ItemsSource = rows;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title      = "Export Statement of Cash Flows",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"CashFlowStatement_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Label,Amount");
                foreach (var row in _rows)
                {
                    var amountStr = row.Amount is decimal amt ? amt.ToString("F2") : "";
                    sb.AppendLine($"{CsvEscape(row.Label)},{amountStr}");
                }

                File.WriteAllText(saveDlg.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Statement of Cash Flows exported successfully:\n{saveDlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(string s) =>
            s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        private static List<(string Name, string Portfolio)> LoadNonBankAccounts(SqliteConnection conn)
        {
            var list = new List<(string, string)>();
            using var cmd = new SqliteCommand(@"
                SELECT AccountName, Portfolio FROM AccountChart
                WHERE AccountType != 'Bank'
                ORDER BY
                    CASE AccountType
                        WHEN 'Investment'      THEN 1
                        WHEN 'Other Asset'     THEN 2
                        WHEN 'Credit Card'     THEN 3
                        WHEN 'Loan'            THEN 4
                        WHEN 'Other Liability' THEN 5
                        WHEN 'Income'          THEN 6
                        WHEN 'Expense'         THEN 7
                        ELSE 8
                    END,
                    AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            return list;
        }

        // Account-column matches count amounts with accountSideTrn; Category-column
        // matches (on both the account name and its Portfolio value, if any) count
        // amounts with categorySideTrn.
        private static decimal SumFlow(SqliteConnection conn, string account, string portfolio,
                                       string startStr, string endStr,
                                       string accountSideTrn, string categorySideTrn)
        {
            decimal total = 0;

            using (var cmd = new SqliteCommand(@"
                SELECT COALESCE(SUM(Amount), 0)
                FROM   Transactions
                WHERE  Account = @acct
                  AND  TrnType = @trn
                  AND  Date >= @start AND Date <= @end;", conn))
            {
                cmd.Parameters.AddWithValue("@acct",  account);
                cmd.Parameters.AddWithValue("@trn",   accountSideTrn);
                cmd.Parameters.AddWithValue("@start", startStr);
                cmd.Parameters.AddWithValue("@end",   endStr);
                total += Convert.ToDecimal(cmd.ExecuteScalar());
            }

            using (var cmd = new SqliteCommand(@"
                SELECT COALESCE(SUM(Amount), 0)
                FROM   Transactions
                WHERE  Category = @acct
                  AND  TrnType = @trn
                  AND  Date >= @start AND Date <= @end;", conn))
            {
                cmd.Parameters.AddWithValue("@acct",  account);
                cmd.Parameters.AddWithValue("@trn",   categorySideTrn);
                cmd.Parameters.AddWithValue("@start", startStr);
                cmd.Parameters.AddWithValue("@end",   endStr);
                total += Convert.ToDecimal(cmd.ExecuteScalar());
            }

            if (!string.IsNullOrEmpty(portfolio) && portfolio != account)
            {
                using var cmd = new SqliteCommand(@"
                    SELECT COALESCE(SUM(Amount), 0)
                    FROM   Transactions
                    WHERE  Category = @port
                      AND  TrnType = @trn
                      AND  Date >= @start AND Date <= @end;", conn);
                cmd.Parameters.AddWithValue("@port",  portfolio);
                cmd.Parameters.AddWithValue("@trn",   categorySideTrn);
                cmd.Parameters.AddWithValue("@start", startStr);
                cmd.Parameters.AddWithValue("@end",   endStr);
                total += Convert.ToDecimal(cmd.ExecuteScalar());
            }

            return total;
        }

        // Combined balance of all Bank accounts through the given date:
        // Account-side rows count DEBIT as deposit / CREDIT as withdrawal;
        // Category-side rows count CREDIT as deposit / DEBIT as withdrawal.
        private static decimal BankBalance(SqliteConnection conn, string throughDate)
        {
            var banks = new List<string>();
            using (var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE AccountType = 'Bank';", conn))
            {
                using var r = cmd.ExecuteReader();
                while (r.Read()) banks.Add(r.GetString(0));
            }

            decimal balance = 0;
            foreach (var bank in banks)
            {
                using (var cmd = new SqliteCommand(@"
                    SELECT COALESCE(SUM(CASE WHEN TrnType = 'DEBIT'  THEN  Amount
                                             WHEN TrnType = 'CREDIT' THEN -Amount
                                             ELSE 0 END), 0)
                    FROM   Transactions
                    WHERE  Account = @acct AND Date <= @d;", conn))
                {
                    cmd.Parameters.AddWithValue("@acct", bank);
                    cmd.Parameters.AddWithValue("@d",    throughDate);
                    balance += Convert.ToDecimal(cmd.ExecuteScalar());
                }

                using (var cmd = new SqliteCommand(@"
                    SELECT COALESCE(SUM(CASE WHEN TrnType = 'CREDIT' THEN  Amount
                                             WHEN TrnType = 'DEBIT'  THEN -Amount
                                             ELSE 0 END), 0)
                    FROM   Transactions
                    WHERE  Category = @acct AND Account != @acct AND Date <= @d;", conn))
                {
                    cmd.Parameters.AddWithValue("@acct", bank);
                    cmd.Parameters.AddWithValue("@d",    throughDate);
                    balance += Convert.ToDecimal(cmd.ExecuteScalar());
                }
            }

            return balance;
        }
    }
}
