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
    public class NetWorthRow
    {
        public string  Label         { get; set; } = "";
        public decimal? Amount       { get; set; }
        public bool    IsBold        { get; set; }
        public bool    IsIndented    { get; set; }

        public string AmountDisplay => Amount?.ToString("N2") ?? "";
        public string Weight        => IsBold ? "Bold" : "Normal";
        public string RowMargin     => IsIndented ? "0,1,0,1" : "0,4,0,2";
        public string LabelMargin   => IsIndented ? "16,0,0,0" : "0,0,0,0";
    }

    public partial class NetWorthView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly DateTime _asOf;

        private readonly List<NetWorthRow> _rows = new();

        public NetWorthView(DatabaseManager db, DateTime asOf)
        {
            _db   = db;
            _asOf = asOf;
            InitializeComponent();
            TxtTitle2.Text = $"As of {_asOf:yyyy-MM-dd}";
            LoadData();
        }

        public void Refresh() => LoadData();

        // ── report build ─────────────────────────────────────────────────

        private void LoadData()
        {
            _rows.Clear();
            string dateStr = _asOf.ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();

            var assets      = LoadAccounts(conn, "Asset",
                                  "CASE AccountType WHEN 'Bank' THEN 1 WHEN 'Investment' THEN 2 WHEN 'Other Asset' THEN 3 ELSE 4 END");
            var liabilities = LoadAccounts(conn, "Liability",
                                  "CASE AccountType WHEN 'Credit Card' THEN 1 WHEN 'Loan' THEN 2 WHEN 'Other Liability' THEN 3 ELSE 4 END");

            // ── Assets ──
            _rows.Add(new NetWorthRow { Label = "Assets", IsBold = true });

            decimal totalAssets = 0;
            foreach (var (name, code, portfolio) in assets)
            {
                decimal bal = AccountBalance(conn, name, dateStr);
                if (code == 2)
                    bal += PortfolioMarketValue(conn, portfolio, dateStr);

                totalAssets += bal;
                if (bal != 0)
                    _rows.Add(new NetWorthRow { Label = name, Amount = bal, IsIndented = true });
            }

            _rows.Add(new NetWorthRow { Label = "Total assets", Amount = totalAssets, IsBold = true });

            // ── Liabilities (shown positive) ──
            _rows.Add(new NetWorthRow { Label = "Liabilities", IsBold = true });

            decimal totalLiabilities = 0;
            foreach (var (name, code, portfolio) in liabilities)
            {
                decimal bal = AccountBalance(conn, name, dateStr);
                if (code == 2)
                    bal += PortfolioMarketValue(conn, portfolio, dateStr);

                var shown = -bal;                 // liability balances display as positive
                totalLiabilities += shown;
                if (shown != 0)
                    _rows.Add(new NetWorthRow { Label = name, Amount = shown, IsIndented = true });
            }

            _rows.Add(new NetWorthRow { Label = "Total liabilities", Amount = totalLiabilities, IsBold = true });

            // ── Net worth ──
            _rows.Add(new NetWorthRow
            {
                Label  = "Net worth",
                Amount = totalAssets - totalLiabilities,
                IsBold = true
            });

            RowsHost.ItemsSource = null;
            RowsHost.ItemsSource = _rows;
        }

        private static List<(string Name, int Code, string Portfolio)> LoadAccounts(
            SqliteConnection conn, string group, string typeOrder)
        {
            var list = new List<(string, int, string)>();
            using var cmd = new SqliteCommand(
                "SELECT AccountName, AccountCode, Portfolio FROM AccountChart " +
                "WHERE AccountGroup = @grp " +
                $"ORDER BY {typeOrder}, AccountName;", conn);
            cmd.Parameters.AddWithValue("@grp", group);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name      = r.IsDBNull(0) ? "" : r.GetString(0);
                var code      = r.IsDBNull(1) ? 0  : Convert.ToInt32(r.GetDouble(1));
                var portfolio = r.IsDBNull(2) ? "" : r.GetString(2);
                list.Add((name, code, portfolio));
            }
            return list;
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

        // Market value of all securities held in the portfolio on the given date,
        // using the same approach as PortfolioView: quantity = Buy - Sell (through
        // the date), split ratio applied from Securities, price from Quotes on the
        // date or the closest prior date.
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

                // Split info from Securities
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

                // Price on the date, or closest prior date
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

        // ── export ───────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title      = "Export Net Worth Statement",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"NetWorth_{_asOf:yyyyMMdd}.csv",
                DefaultExt = ".csv"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Statement of Net Worth");
                sb.AppendLine($"As of {_asOf:yyyy-MM-dd}");
                sb.AppendLine();

                foreach (var row in _rows)
                {
                    var label = row.Label.Contains(',')
                        ? $"\"{row.Label.Replace("\"", "\"\"")}\""
                        : row.Label;
                    sb.AppendLine(row.Amount is decimal amt
                        ? $"{label},{amt:F2}"
                        : label);
                }

                File.WriteAllText(saveDlg.FileName, sb.ToString());

                MessageBox.Show($"Report exported successfully:\n{saveDlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
