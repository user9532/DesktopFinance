using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class CashFlowView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly DateTime _start;
        private readonly DateTime _end;

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

            // ── Cash Inflows: Account-side DEBIT + Category-side DEBIT ──
            rows.Add(new NetWorthRow { Label = "Cash Inflows", IsBold = true });

            decimal totalInflows = 0;
            foreach (var (name, portfolio) in accounts)
            {
                var amt = SumFlow(conn, name, startStr, endStr,
                                  accountSideTrn: "DEBIT", categorySideTrn: "DEBIT");
                totalInflows += amt;
                if (amt != 0)
                    rows.Add(new NetWorthRow { Label = name, Amount = amt, IsIndented = true });

                // Portfolio flows are reversed: CREDIT counts as inflow
                if (!string.IsNullOrEmpty(portfolio))
                {
                    var pAmt = SumFlow(conn, portfolio, startStr, endStr,
                                       accountSideTrn: "CREDIT", categorySideTrn: "CREDIT");
                    totalInflows += pAmt;
                    if (pAmt != 0)
                        rows.Add(new NetWorthRow { Label = portfolio, Amount = pAmt, IsIndented = true });
                }
            }
            rows.Add(new NetWorthRow { Label = "Total Cash Inflows", Amount = totalInflows, IsBold = true });

            // ── Cash Outflows: Account-side CREDIT + Category-side CREDIT ──
            rows.Add(new NetWorthRow { Label = "Cash Outflows", IsBold = true });

            decimal totalOutflows = 0;
            foreach (var (name, portfolio) in accounts)
            {
                var amt = SumFlow(conn, name, startStr, endStr,
                                  accountSideTrn: "CREDIT", categorySideTrn: "CREDIT");
                totalOutflows += amt;
                if (amt != 0)
                    rows.Add(new NetWorthRow { Label = name, Amount = amt, IsIndented = true });

                // Portfolio flows are reversed: DEBIT counts as outflow
                if (!string.IsNullOrEmpty(portfolio))
                {
                    var pAmt = SumFlow(conn, portfolio, startStr, endStr,
                                       accountSideTrn: "DEBIT", categorySideTrn: "DEBIT");
                    totalOutflows += pAmt;
                    if (pAmt != 0)
                        rows.Add(new NetWorthRow { Label = portfolio, Amount = pAmt, IsIndented = true });
                }
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

            RowsHost.ItemsSource = rows;
        }

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
        // matches count amounts with categorySideTrn.
        private static decimal SumFlow(SqliteConnection conn, string account,
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
