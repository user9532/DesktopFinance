using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class BudgetActualView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly DateTime _start;
        private readonly DateTime _end;

        private List<BudgetLine> _incomeLines  = new();
        private List<BudgetLine> _expenseLines = new();

        private decimal _incomeBudget;
        private decimal _incomeActual;
        private decimal _expenseBudget;
        private decimal _expenseActual;

        public BudgetActualView(DatabaseManager db, DateTime start, DateTime end)
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

            _incomeLines  = LoadGroup("Income",  startStr, endStr, incomeSign: true);
            _expenseLines = LoadGroup("Expense", startStr, endStr, incomeSign: false);

            _incomeBudget  = _incomeLines.Sum(l => l.Budget);
            _incomeActual  = _incomeLines.Sum(l => l.Actual);
            _expenseBudget = _expenseLines.Sum(l => l.Budget);
            _expenseActual = _expenseLines.Sum(l => l.Actual);

            IncomeList.ItemsSource  = _incomeLines;
            ExpenseList.ItemsSource = _expenseLines;

            TbIncomeBudget.Text   = _incomeBudget.ToString("N2");
            TbIncomeActual.Text   = _incomeActual.ToString("N2");
            TbIncomeVariance.Text = (_incomeBudget - _incomeActual).ToString("N2");

            TbExpenseBudget.Text   = _expenseBudget.ToString("N2");
            TbExpenseActual.Text   = _expenseActual.ToString("N2");
            TbExpenseVariance.Text = (_expenseBudget - _expenseActual).ToString("N2");

            TbExcessBudget.Text   = (_incomeBudget  - _expenseBudget).ToString("N2");
            TbExcessActual.Text   = (_incomeActual  - _expenseActual).ToString("N2");
            TbExcessVariance.Text = ((_incomeBudget - _expenseBudget) - (_incomeActual - _expenseActual)).ToString("N2");
        }

        private List<BudgetLine> LoadGroup(string accountGroup, string startStr, string endStr,
                                           bool incomeSign)
        {
            var lines = new List<BudgetLine>();
            using var conn = _db.OpenConnection();

            // Fetch account names for this group.
            using var acctCmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountGroup = @grp ORDER BY AccountName;", conn);
            acctCmd.Parameters.AddWithValue("@grp", accountGroup);

            var accounts = new List<string>();
            using (var r = acctCmd.ExecuteReader())
                while (r.Read()) accounts.Add(r.GetString(0));

            foreach (var account in accounts)
            {
                // Actual from Transactions.
                using var actualCmd = new SqliteCommand(@"
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

                actualCmd.Parameters.AddWithValue("@cat",   account);
                actualCmd.Parameters.AddWithValue("@start", startStr);
                actualCmd.Parameters.AddWithValue("@end",   endStr);
                actualCmd.Parameters.AddWithValue("@pos",   incomeSign ? "DEBIT"  : "CREDIT");
                actualCmd.Parameters.AddWithValue("@neg",   incomeSign ? "CREDIT" : "DEBIT");

                var actualRaw = actualCmd.ExecuteScalar();
                var actual    = actualRaw == null || actualRaw == DBNull.Value
                                ? 0m : Convert.ToDecimal(actualRaw);

                // Budget: sum Budget column for all months that overlap the date range.
                // Dates are stored as yyyy-MM-dd (first of month); match with >= / <=
                // using the first day of the start month and first day of the end month.
                var budgetStart = new DateTime(_start.Year, _start.Month, 1).ToString("yyyy-MM-dd");
                var budgetEnd   = new DateTime(_end.Year,   _end.Month,   1).ToString("yyyy-MM-dd");

                using var budgetCmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(Budget), 0) FROM Budget " +
                    "WHERE Category = @cat AND Date >= @bs AND Date <= @be;", conn);
                budgetCmd.Parameters.AddWithValue("@cat", account);
                budgetCmd.Parameters.AddWithValue("@bs",  budgetStart);
                budgetCmd.Parameters.AddWithValue("@be",  budgetEnd);

                var budgetRaw = budgetCmd.ExecuteScalar();
                decimal budget = budgetRaw == null || budgetRaw == DBNull.Value
                                 ? 0m : Convert.ToDecimal(budgetRaw);

                if (budget != 0 || actual != 0)
                    lines.Add(new BudgetLine(account, budget, actual));
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
                FileName   = $"BudgetActual_{_start:yyyyMMdd}_{_end:yyyyMMdd}.csv",
                DefaultExt = ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Budget versus Actual");
                sb.AppendLine($"For the period,{_start:MM/dd/yyyy},{_end:MM/dd/yyyy}");
                sb.AppendLine();
                sb.AppendLine("Category,Budget,Actual,Variance");

                sb.AppendLine("Income");
                foreach (var l in _incomeLines)
                    sb.AppendLine($"{CsvEscape(l.Name)},{l.Budget:F2},{l.Actual:F2},{l.Variance:F2}");
                sb.AppendLine($"Total Income,{_incomeBudget:F2},{_incomeActual:F2},{(_incomeBudget - _incomeActual):F2}");
                sb.AppendLine();

                sb.AppendLine("Expenses");
                foreach (var l in _expenseLines)
                    sb.AppendLine($"{CsvEscape(l.Name)},{l.Budget:F2},{l.Actual:F2},{l.Variance:F2}");
                sb.AppendLine($"Total Expenses,{_expenseBudget:F2},{_expenseActual:F2},{(_expenseBudget - _expenseActual):F2}");
                sb.AppendLine();

                sb.AppendLine($"Excess income over expenses," +
                              $"{(_incomeBudget - _expenseBudget):F2}," +
                              $"{(_incomeActual - _expenseActual):F2}," +
                              $"{((_incomeBudget - _expenseBudget) - (_incomeActual - _expenseActual)):F2}");

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

        private class BudgetLine
        {
            public string  Name            { get; }
            public decimal Budget          { get; }
            public decimal Actual          { get; }
            public decimal Variance        => Budget - Actual;
            public string  BudgetDisplay   => Budget.ToString("N2");
            public string  ActualDisplay   => Actual.ToString("N2");
            public string  VarianceDisplay => Variance.ToString("N2");

            public BudgetLine(string name, decimal budget, decimal actual)
            {
                Name   = name;
                Budget = budget;
                Actual = actual;
            }
        }
    }
}
