using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public class PendingTransRow
    {
        public string  Payee   { get; set; } = "";
        public decimal Amount  { get; set; }
        public string  NextDue { get; set; } = "";

        public string AmountDisplay => Amount.ToString("N2");
    }

    public class AccountBalanceRow
    {
        public string  Name    { get; set; } = "";
        public decimal Balance { get; set; }

        public string BalanceDisplay => Balance.ToString("N2");
    }

    public class DashboardBarRow
    {
        public string  Category { get; set; } = "";
        public decimal Budget   { get; set; }
        public decimal Actual   { get; set; }

        public string BudgetDisplay => Budget.ToString("N2");
        public string ActualDisplay => Actual.ToString("N2");

        // Pixel widths scaled against the largest Budget/Actual value across all rows.
        public double BudgetBarWidth { get; set; }
        public double ActualBarWidth { get; set; }
    }

    public partial class HomeView : UserControl
    {
        private readonly DatabaseManager _db;

        private const double MaxBarWidth = 400;

        public HomeView(DatabaseManager db)
        {
            _db = db;
            InitializeComponent();
            LoadPendingTransactions();
            LoadAccountBalances();
            LoadDashboard();
        }

        public void Refresh()
        {
            LoadPendingTransactions();
            LoadAccountBalances();
            LoadDashboard();
        }

        // ── bank / credit card / investment / loan balances ─────────────────

        private void LoadAccountBalances()
        {
            using var conn = _db.OpenConnection();
            var today = DateTime.Today.ToString("yyyy-MM-dd");

            var bankRows = LoadAccountBalancesForType(conn, "Bank", today);
            BankBalanceList.ItemsSource = bankRows;
            TxtNoBankAccounts.Visibility = bankRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var ccRows = LoadAccountBalancesForType(conn, "Credit Card", today);
            CreditCardBalanceList.ItemsSource = ccRows;
            TxtNoCreditCards.Visibility = ccRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var investmentRows = LoadInvestmentBalances(conn, today);
            InvestmentBalanceList.ItemsSource = investmentRows;
            TxtNoInvestmentAccounts.Visibility = investmentRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var loanRows = LoadAccountBalancesForType(conn, "Loan", today);
            LoanBalanceList.ItemsSource = loanRows;
            TxtNoLoanAccounts.Visibility = loanRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Investment account balance = cash balance (same as AccountBalance) plus
        // the market value of securities held in the account's Portfolio — the same
        // approach used for Investment accounts (AccountCode = 2) in NetWorthView.
        private static List<AccountBalanceRow> LoadInvestmentBalances(SqliteConnection conn, string throughDate)
        {
            var accounts = new List<(string Name, string Portfolio)>();
            using (var cmd = new SqliteCommand(
                "SELECT AccountName, Portfolio FROM AccountChart " +
                "WHERE AccountType = 'Investment' ORDER BY AccountName;", conn))
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    accounts.Add((r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            }

            var rows = new List<AccountBalanceRow>();
            foreach (var (name, portfolio) in accounts)
            {
                var balance = AccountBalance(conn, name, throughDate)
                            + PortfolioMarketValue(conn, portfolio, throughDate);
                rows.Add(new AccountBalanceRow { Name = name, Balance = balance });
            }
            return rows;
        }

        // Market value of all securities held in the portfolio on the given date:
        // quantity = Buy - Sell (through the date), split ratio applied from
        // Securities, price from Quotes on the date or the closest prior date.
        private static decimal PortfolioMarketValue(SqliteConnection conn, string portfolio, string dateStr)
        {
            if (string.IsNullOrEmpty(portfolio)) return 0;

            var symbolQtys = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqliteCommand(@"
                SELECT Symbol, Action, SUM(Quantity)
                FROM   Transactions
                WHERE  Category = @p
                  AND  Symbol IS NOT NULL AND Symbol != ''
                  AND  Action IN ('Buy','Sell')
                  AND  Date <= @d
                GROUP  BY Symbol, Action;", conn))
            {
                cmd.Parameters.AddWithValue("@p", portfolio);
                cmd.Parameters.AddWithValue("@d", dateStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var sym    = r.GetString(0);
                    var action = r.GetString(1);
                    var qty    = r.IsDBNull(2) ? 0m : (decimal)r.GetDouble(2);
                    if (!symbolQtys.ContainsKey(sym)) symbolQtys[sym] = 0;
                    symbolQtys[sym] += action == "Buy" ? qty : -qty;
                }
            }

            decimal total = 0;

            foreach (var kv in symbolQtys)
            {
                var sym = kv.Key;
                var qty = kv.Value;
                if (qty <= 0) continue;

                bool    isSplit    = false;
                decimal splitRatio = 1m;
                using (var cmd = new SqliteCommand(
                    "SELECT IsSplit, SplitRatio FROM Securities WHERE Symbol = @s;", conn))
                {
                    cmd.Parameters.AddWithValue("@s", sym);
                    using var sr = cmd.ExecuteReader();
                    if (sr.Read())
                    {
                        isSplit    = !sr.IsDBNull(0) && sr.GetInt32(0) == 1;
                        splitRatio = (isSplit && !sr.IsDBNull(1)) ? (decimal)sr.GetDouble(1) : 1m;
                    }
                }
                if (isSplit && splitRatio > 0)
                    qty *= splitRatio;

                decimal price = 0;
                using (var cmd = new SqliteCommand(@"
                    SELECT Price FROM Quotes
                    WHERE  Symbol = @s AND Date <= @d
                    ORDER  BY Date DESC LIMIT 1;", conn))
                {
                    cmd.Parameters.AddWithValue("@s", sym);
                    cmd.Parameters.AddWithValue("@d", dateStr);
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value)
                        price = (decimal)(double)val;
                }

                total += qty * price;
            }

            return total;
        }

        private static List<AccountBalanceRow> LoadAccountBalancesForType(
            SqliteConnection conn, string accountType, string throughDate)
        {
            var names = new List<string>();
            using (var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE AccountType = @t ORDER BY AccountName;", conn))
            {
                cmd.Parameters.AddWithValue("@t", accountType);
                using var r = cmd.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
            }

            var rows = new List<AccountBalanceRow>();
            foreach (var name in names)
                rows.Add(new AccountBalanceRow { Name = name, Balance = AccountBalance(conn, name, throughDate) });
            return rows;
        }

        // Account-side rows: DEBIT positive, CREDIT negative.
        // Category-side rows: CREDIT positive, DEBIT negative (excluding self-matched rows).
        private static decimal AccountBalance(SqliteConnection conn, string account, string dateStr)
        {
            decimal bal = 0;

            using (var cmd = new SqliteCommand(@"
                SELECT COALESCE(SUM(CASE WHEN TrnType = 'CREDIT' THEN -Amount ELSE Amount END), 0)
                FROM   Transactions
                WHERE  Account = @acct AND Date <= @d;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", account);
                cmd.Parameters.AddWithValue("@d",    dateStr);
                bal += Convert.ToDecimal(cmd.ExecuteScalar());
            }

            using (var cmd = new SqliteCommand(@"
                SELECT COALESCE(SUM(CASE WHEN TrnType = 'DEBIT' THEN -Amount ELSE Amount END), 0)
                FROM   Transactions
                WHERE  Category = @acct AND Account != @acct AND Date <= @d;", conn))
            {
                cmd.Parameters.AddWithValue("@acct", account);
                cmd.Parameters.AddWithValue("@d",    dateStr);
                bal += Convert.ToDecimal(cmd.ExecuteScalar());
            }

            return bal;
        }

        // ── pending transactions ────────────────────────────────────────────

        private void LoadPendingTransactions()
        {
            var payments = new List<PendingTransRow>();
            var receipts = new List<PendingTransRow>();

            var horizon = DateTime.Today.AddDays(30);
            var horizonStr = horizon.ToString("yyyy-MM-dd");

            // Include overdue items (NextDue before today) as well as items due
            // within the next 30 days — not just ones due today or later.
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Name, Amount, NextDue, TrType FROM RecurTrans " +
                "WHERE TrType IN (1, 2, 3) AND NextDue IS NOT NULL AND NextDue != '' AND NextDue <= @horizon " +
                "ORDER BY NextDue, Name;", conn);
            cmd.Parameters.AddWithValue("@horizon", horizonStr);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new PendingTransRow
                {
                    Payee   = r.IsDBNull(0) ? "" : r.GetString(0),
                    Amount  = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1),
                    NextDue = r.IsDBNull(2) ? "" : r.GetString(2),
                };
                var trType = r.IsDBNull(3) ? 1 : r.GetInt32(3);

                if (trType == 2) receipts.Add(row);
                else             payments.Add(row);   // 1 = payment, 3 = loan payment
            }

            PendingPaymentsList.ItemsSource = payments;
            TxtNoPayments.Visibility = payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            PendingReceiptsList.ItemsSource = receipts;
            TxtNoReceipts.Visibility = receipts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ViewRecurring_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow main)
                main.ActivateOrOpenRecurringPublic();
        }

        // ── dashboard: budget vs actual expense, latest available month ────

        private void LoadDashboard()
        {
            using var conn = _db.OpenConnection();

            // Current month if it has data; otherwise the latest previous month that does.
            var currentMonthStr = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
            string? latestMonth = HasDataForMonth(conn, currentMonthStr)
                ? currentMonthStr.Substring(0, 7)
                : LatestPriorMonthWithData(conn, currentMonthStr);
            if (latestMonth == null)
            {
                TxtDashboardSubtitle.Text = "";
                DashboardChart.ItemsSource = null;
                TxtNoDashboardData.Visibility = Visibility.Visible;
                return;
            }

            var monthStart = DateTime.Parse(latestMonth + "-01");
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1);
            TxtDashboardSubtitle.Text = monthStart.ToString("MMMM yyyy");

            var rows = new List<DashboardBarRow>();

            using var acctCmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE AccountGroup = 'Expense' ORDER BY AccountName;", conn);
            var categories = new List<string>();
            using (var r = acctCmd.ExecuteReader())
                while (r.Read()) categories.Add(r.GetString(0));

            var budgetMonthStr = new DateTime(monthStart.Year, monthStart.Month, 1).ToString("yyyy-MM-dd");
            var startStr = monthStart.ToString("yyyy-MM-dd");
            var endStr   = monthEnd.ToString("yyyy-MM-dd");

            foreach (var category in categories)
            {
                using var budgetCmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(Budget), 0) FROM Budget WHERE Category = @cat AND Date = @m;", conn);
                budgetCmd.Parameters.AddWithValue("@cat", category);
                budgetCmd.Parameters.AddWithValue("@m",   budgetMonthStr);
                var budget = Convert.ToDecimal(budgetCmd.ExecuteScalar());

                using var actualCmd = new SqliteCommand(@"
                    SELECT COALESCE(SUM(CASE WHEN TrnType = 'CREDIT' THEN Amount ELSE -Amount END), 0)
                    FROM Transactions
                    WHERE Category = @cat AND Date >= @start AND Date <= @end;", conn);
                actualCmd.Parameters.AddWithValue("@cat",   category);
                actualCmd.Parameters.AddWithValue("@start", startStr);
                actualCmd.Parameters.AddWithValue("@end",   endStr);
                var actual = Convert.ToDecimal(actualCmd.ExecuteScalar());

                if (budget != 0 || actual != 0)
                    rows.Add(new DashboardBarRow { Category = category, Budget = budget, Actual = actual });
            }

            var maxVal = rows.Count == 0 ? 0m : rows.Max(x => Math.Max(x.Budget, x.Actual));
            foreach (var row in rows)
            {
                row.BudgetBarWidth = maxVal > 0 ? (double)(row.Budget / maxVal) * MaxBarWidth : 0;
                row.ActualBarWidth = maxVal > 0 ? (double)(row.Actual / maxVal) * MaxBarWidth : 0;
            }

            DashboardChart.ItemsSource = rows;
            TxtNoDashboardData.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Whether the given month (yyyy-MM-01) has a Budget entry or an actual
        // Expense transaction anywhere within that month.
        private static bool HasDataForMonth(SqliteConnection conn, string monthStartStr)
        {
            var monthStart = DateTime.Parse(monthStartStr);
            var monthEnd   = monthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd");

            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM Budget WHERE Date = @m;", conn))
            {
                cmd.Parameters.AddWithValue("@m", monthStartStr);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) return true;
            }

            using (var cmd = new SqliteCommand(@"
                SELECT COUNT(*) FROM Transactions
                WHERE Category IN (SELECT AccountName FROM AccountChart WHERE AccountGroup = 'Expense')
                  AND Date >= @start AND Date <= @end;", conn))
            {
                cmd.Parameters.AddWithValue("@start", monthStartStr);
                cmd.Parameters.AddWithValue("@end",   monthEnd);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) return true;
            }

            return false;
        }

        // Latest month strictly before beforeMonthStr (yyyy-MM-01) that has a Budget
        // entry or an actual Expense transaction. Returns yyyy-MM, or null if none.
        private static string? LatestPriorMonthWithData(SqliteConnection conn, string beforeMonthStr)
        {
            string? latestBudget;
            using (var cmd = new SqliteCommand(
                "SELECT MAX(Date) FROM Budget WHERE Date < @m;", conn))
            {
                cmd.Parameters.AddWithValue("@m", beforeMonthStr);
                latestBudget = cmd.ExecuteScalar() as string;
            }

            string? latestExpense;
            using (var cmd = new SqliteCommand(@"
                SELECT MAX(Date) FROM Transactions
                WHERE Category IN (SELECT AccountName FROM AccountChart WHERE AccountGroup = 'Expense')
                  AND Date < @m;", conn))
            {
                cmd.Parameters.AddWithValue("@m", beforeMonthStr);
                var raw = cmd.ExecuteScalar() as string;
                latestExpense = string.IsNullOrEmpty(raw) ? null : raw.Substring(0, 7) + "-01";
            }

            var candidates = new[] { latestBudget, latestExpense }
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            if (candidates.Count == 0) return null;

            var latest = candidates.Max()!;
            return latest.Substring(0, 7);   // yyyy-MM
        }
    }
}
