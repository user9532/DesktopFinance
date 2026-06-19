using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class PortfolioView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _portfolio;

        public PortfolioView(DatabaseManager db, string portfolio)
        {
            _db        = db;
            _portfolio = portfolio;
            InitializeComponent();
            LoadData();
        }

        public void Refresh() => LoadData();

        private void InvestmentAccount_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow main)
                main.ActivateOrOpenInvestmentStatement(_portfolio);
        }

        private void LoadData()
        {
            var rows = new ObservableCollection<PortfolioRowViewModel>();

            using var conn = _db.OpenConnection();

            // ── 1. Find the investment account that owns this portfolio ──────
            string investmentAccount = "";
            using (var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE Portfolio = @p AND AccountCode = 2 LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@p", _portfolio);
                investmentAccount = cmd.ExecuteScalar() as string ?? "";
            }

            // ── 2. Cash row: running balance of the investment account ───────
            decimal cashBalance = 0;
            if (!string.IsNullOrEmpty(investmentAccount))
            {
                using var cmd = new SqliteCommand(@"
                    SELECT TrnType, Amount, Account
                    FROM   Transactions
                    WHERE  Account = @acct OR Category = @acct
                    ORDER  BY Date ASC;", conn);
                cmd.Parameters.AddWithValue("@acct", investmentAccount);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var trnType = r.IsDBNull(0) ? "" : r.GetString(0);
                    var rawAmt  = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);
                    var acctCol = r.IsDBNull(2) ? "" : r.GetString(2);
                    // Rows matched via Category column have inverted sign
                    if (acctCol != investmentAccount)
                        trnType = trnType == "DEBIT" ? "CREDIT" : "DEBIT";
                    cashBalance += trnType == "CREDIT" ? -rawAmt : rawAmt;
                }
            }

            var cashRow = new PortfolioRowViewModel
            {
                Name         = "Cash",
                Symbol       = "",
                CurrentValue = cashBalance,
                CostBasis    = cashBalance,
                IsCash       = true
            };
            rows.Add(cashRow);

            // ── 3. Per-symbol quantity balances (Category = portfolio) ───────
            var symbolQtys = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqliteCommand(@"
                SELECT Symbol, Action, SUM(Quantity)
                FROM   Transactions
                WHERE  Category = @p
                  AND  Symbol IS NOT NULL AND Symbol != ''
                  AND  Action IN ('Buy','Sell')
                GROUP  BY Symbol, Action;", conn))
            {
                cmd.Parameters.AddWithValue("@p", _portfolio);
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

            string today = DateTime.Today.ToString("yyyy-MM-dd");

            foreach (var kv in symbolQtys)
            {
                var sym = kv.Key;
                var qty = kv.Value;
                if (qty <= 0) continue;

                // Description and split info from Securities
                string  description = sym;
                bool    isSplit     = false;
                decimal splitRatio  = 1m;
                using (var cmd = new SqliteCommand(
                    "SELECT Description, IsSplit, SplitRatio FROM Securities WHERE Symbol = @s;", conn))
                {
                    cmd.Parameters.AddWithValue("@s", sym);
                    using var sr = cmd.ExecuteReader();
                    if (sr.Read())
                    {
                        description = sr.IsDBNull(0) ? sym : sr.GetString(0);
                        isSplit     = !sr.IsDBNull(1) && sr.GetInt32(1) == 1;
                        splitRatio  = (isSplit && !sr.IsDBNull(2)) ? (decimal)sr.GetDouble(2) : 1m;
                    }
                }

                // Apply split: effective quantity is scaled by the split ratio
                if (isSplit && splitRatio > 0)
                    qty *= splitRatio;

                // Latest price from Quotes on or before today
                decimal price = 0;
                using (var cmd = new SqliteCommand(@"
                    SELECT Price FROM Quotes
                    WHERE  Symbol = @s AND Date <= @d
                    ORDER  BY Date DESC LIMIT 1;", conn))
                {
                    cmd.Parameters.AddWithValue("@s", sym);
                    cmd.Parameters.AddWithValue("@d", today);
                    var val = cmd.ExecuteScalar();
                    if (val != null && val != DBNull.Value)
                        price = (decimal)(double)val;
                }

                var currentValue = qty * price;

                // Cost basis: sum of (Quantity * Price + Commission) for Buy lots
                // reduced proportionally if some shares were sold.
                // Full lot cost basis then scale by qty/totalBought.
                decimal totalBought  = 0;
                decimal totalCostRaw = 0;
                using (var cmd = new SqliteCommand(@"
                    SELECT Quantity, Price, Commission
                    FROM   Transactions
                    WHERE  Category = @p AND Symbol = @s AND Action = 'Buy';", conn))
                {
                    cmd.Parameters.AddWithValue("@p", _portfolio);
                    cmd.Parameters.AddWithValue("@s", sym);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        var bQty   = r.IsDBNull(0) ? 0m : (decimal)r.GetDouble(0);
                        var bPrice = r.IsDBNull(1) ? 0m : (decimal)r.GetDouble(1);
                        var bComm  = r.IsDBNull(2) ? 0m : (decimal)r.GetDouble(2);
                        totalBought  += bQty;
                        totalCostRaw += bQty * bPrice + bComm;
                    }
                }

                decimal costBasis = totalBought > 0
                    ? totalCostRaw * (qty / totalBought)
                    : 0;

                rows.Add(new PortfolioRowViewModel
                {
                    Name         = description,
                    Symbol       = sym,
                    Quantity     = qty,
                    Price        = price,
                    CurrentValue = currentValue,
                    CostBasis    = costBasis
                });
            }

            // ── 4. Total current value, fill % of Total, add Total row ─────
            decimal totalValue    = 0;
            decimal totalCost     = 0;
            decimal totalGainLoss = 0;
            foreach (var row in rows)
            {
                totalValue    += row.CurrentValue;
                totalCost     += row.CostBasis;
                totalGainLoss += row.CurrentValue - row.CostBasis;
            }

            foreach (var row in rows)
                row.PctOfTotal = totalValue != 0 ? row.CurrentValue / totalValue * 100m : 0;

            decimal totalPctGainLoss = totalCost != 0 ? totalGainLoss / totalCost * 100m : 0;

            rows.Add(new PortfolioRowViewModel
            {
                Name         = "Total",
                CurrentValue = totalValue,
                PctOfTotal   = 100m,
                CostBasis    = totalCost,
                IsTotal      = true,
                TotalGainLoss    = totalGainLoss,
                TotalPctGainLoss = totalPctGainLoss
            });

            PfGrid.ItemsSource = rows;
        }
    }

    public class PortfolioRowViewModel
    {
        public string  Name     { get; set; } = "";
        public string  Symbol   { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal Price    { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal PctOfTotal   { get; set; }
        public decimal CostBasis    { get; set; }
        public bool    IsCash       { get; set; }
        public bool    IsTotal      { get; set; }

        // Overrides for the Total row (computed externally so no double-count issues)
        public decimal TotalGainLoss    { get; set; }
        public decimal TotalPctGainLoss { get; set; }

        public string RowFontWeight => IsTotal ? "Bold" : "Normal";

        private decimal GainLoss    => CurrentValue - CostBasis;
        private decimal PctGainLoss => CostBasis != 0 ? GainLoss / CostBasis * 100m : 0;

        public string QuantityDisplay    => (IsCash || IsTotal) ? "" : Quantity.ToString("N4");
        public string PriceDisplay       => (IsCash || IsTotal) ? "" : Price.ToString("N4");
        public string PctOfTotalDisplay  => PctOfTotal.ToString("N2") + "%";
        public string GainLossDisplay    => IsCash ? "" : IsTotal
                                               ? TotalGainLoss.ToString("N2")
                                               : GainLoss.ToString("N2");
        public string PctGainLossDisplay => IsCash ? "" : IsTotal
                                               ? TotalPctGainLoss.ToString("N2") + "%"
                                               : PctGainLoss.ToString("N2") + "%";
    }
}
