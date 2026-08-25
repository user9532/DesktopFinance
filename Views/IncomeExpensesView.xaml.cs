using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FinanceApp.Views
{
    public partial class IncomeExpensesView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly DateTime _start;
        private readonly DateTime _end;

        private List<ReportLine> _incomeLines  = new();
        private List<ReportLine> _expenseLines = new();
        private decimal _totalIncome;
        private decimal _totalExpenses;

        public IncomeExpensesView(DatabaseManager db, DateTime start, DateTime end)
        {
            InitializeComponent();
            _db    = db;
            _start = start;
            _end   = end;

            TbPeriod.Text = $"For the period  {start:MM/dd/yyyy} – {end:MM/dd/yyyy}";

            BuildReport();
        }

        // ── data ─────────────────────────────────────────────────────────

        private void BuildReport()
        {
            var startStr = _start.ToString("yyyy-MM-dd");
            var endStr   = _end.ToString("yyyy-MM-dd");

            _incomeLines  = LoadGroup("Income",  startStr, endStr, incomeSign:  true);
            _expenseLines = LoadGroup("Expense", startStr, endStr, incomeSign: false);

            _totalIncome   = _incomeLines.Sum(l => l.Amount);
            _totalExpenses = _expenseLines.Sum(l => l.Amount);
            var excess     = _totalIncome - _totalExpenses;

            IncomeList.ItemsSource  = _incomeLines;
            ExpenseList.ItemsSource = _expenseLines;

            TbTotalIncome.Text   = _totalIncome.ToString("N2");
            TbTotalExpenses.Text = _totalExpenses.ToString("N2");
            TbExcess.Text        = excess.ToString("N2");
        }

        private List<ReportLine> LoadGroup(string accountGroup, string startStr, string endStr,
                                           bool incomeSign)
        {
            var lines = new List<ReportLine>();

            using var conn = _db.OpenConnection();

            // Get all AccountNames for the group, alphabetically.
            using var acctCmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountGroup = @grp ORDER BY AccountName;", conn);
            acctCmd.Parameters.AddWithValue("@grp", accountGroup);

            var accounts = new List<string>();
            using (var r = acctCmd.ExecuteReader())
                while (r.Read()) accounts.Add(r.GetString(0));

            foreach (var account in accounts)
            {
                // Sum amounts for this category in the date range.
                // incomeSign=true  (Income):  DEBIT → positive, CREDIT → negative
                // incomeSign=false (Expense): CREDIT → positive, DEBIT  → negative
                using var sumCmd = new SqliteCommand(@"
                    SELECT COALESCE(SUM(
                        CASE
                            WHEN TrnType = @pos THEN  Amount
                            WHEN TrnType = @neg THEN -Amount
                            ELSE 0
                        END), 0)
                    FROM Transactions
                    WHERE Category = @cat
                      AND Date >= @start
                      AND Date <= @end;", conn);

                sumCmd.Parameters.AddWithValue("@cat",   account);
                sumCmd.Parameters.AddWithValue("@start", startStr);
                sumCmd.Parameters.AddWithValue("@end",   endStr);
                sumCmd.Parameters.AddWithValue("@pos",   incomeSign ? "DEBIT"  : "CREDIT");
                sumCmd.Parameters.AddWithValue("@neg",   incomeSign ? "CREDIT" : "DEBIT");

                var raw = sumCmd.ExecuteScalar();
                var amount = raw == null || raw == DBNull.Value ? 0m : Convert.ToDecimal(raw);

                if (amount != 0)
                    lines.Add(new ReportLine(account, amount));
            }

            return lines;
        }

        // ── export ───────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export Report as CSV",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"IncomeExpenses_{_start:yyyyMMdd}_{_end:yyyyMMdd}.csv",
                DefaultExt = ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Income and Expenses Report");
                sb.AppendLine($"For the period,{_start:MM/dd/yyyy},{_end:MM/dd/yyyy}");
                sb.AppendLine();

                sb.AppendLine("Income");
                foreach (var l in _incomeLines)
                    sb.AppendLine($"{CsvEscape(l.Name)},{l.Amount:F2}");
                sb.AppendLine($"Total Income,{_totalIncome:F2}");
                sb.AppendLine();

                sb.AppendLine("Expenses");
                foreach (var l in _expenseLines)
                    sb.AppendLine($"{CsvEscape(l.Name)},{l.Amount:F2}");
                sb.AppendLine($"Total Expenses,{_totalExpenses:F2}");
                sb.AppendLine();

                sb.AppendLine($"Excess income over expenses,{(_totalIncome - _totalExpenses):F2}");

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Report exported to:\n{dlg.FileName}",
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

        // ── view model ───────────────────────────────────────────────────

        private class ReportLine
        {
            public string  Name          { get; }
            public decimal Amount        { get; }
            public string  AmountDisplay => Amount.ToString("N2");

            public ReportLine(string name, decimal amount)
            {
                Name   = name;
                Amount = amount;
            }
        }
    }
}
