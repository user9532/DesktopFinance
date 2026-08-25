using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class InvestmentPerformanceView : UserControl
    {
        private readonly DatabaseManager _db;
        private List<InvestmentPerformanceRowViewModel> _rows = new();

        public InvestmentPerformanceView(DatabaseManager db)
        {
            _db = db;
            InitializeComponent();
            TxtAsOf.Text = $"As of {DateTime.Today:yyyy-MM-dd}";
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var rows = new List<InvestmentPerformanceRowViewModel>();
            string today = DateTime.Today.ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();

            var portfolios = new List<string>();
            using (var cmd = new SqliteCommand(
                "SELECT DISTINCT Portfolio FROM AccountChart WHERE Portfolio IS NOT NULL AND Portfolio != '' ORDER BY Portfolio;", conn))
            {
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    portfolios.Add(r.GetString(0));
            }

            decimal totalValue = 0;
            decimal totalCost  = 0;

            foreach (var portfolio in portfolios)
            {
                var symbolQtys = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = new SqliteCommand(@"
                    SELECT Symbol, Action, SUM(Quantity)
                    FROM   Transactions
                    WHERE  Category = @p
                      AND  Symbol IS NOT NULL AND Symbol != ''
                      AND  Action IN ('Buy','Sell')
                    GROUP  BY Symbol, Action;", conn))
                {
                    cmd.Parameters.AddWithValue("@p", portfolio);
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

                bool addedPortfolioHeader = false;

                foreach (var kv in symbolQtys)
                {
                    var sym = kv.Key;
                    var qty = kv.Value;
                    if (qty <= 0) continue;

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

                    if (isSplit && splitRatio > 0)
                        qty *= splitRatio;

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

                    decimal totalBought  = 0;
                    decimal totalCostRaw = 0;
                    using (var cmd = new SqliteCommand(@"
                        SELECT Quantity, Price, Commission
                        FROM   Transactions
                        WHERE  Category = @p AND Symbol = @s AND Action = 'Buy';", conn))
                    {
                        cmd.Parameters.AddWithValue("@p", portfolio);
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

                    rows.Add(new InvestmentPerformanceRowViewModel
                    {
                        Portfolio    = addedPortfolioHeader ? "" : portfolio,
                        Name         = description,
                        Symbol       = sym,
                        Quantity     = qty,
                        Price        = price,
                        CurrentValue = currentValue,
                        CostBasis    = costBasis
                    });
                    addedPortfolioHeader = true;

                    totalValue += currentValue;
                    totalCost  += costBasis;
                }
            }

            var totalGainLoss    = totalValue - totalCost;
            var totalPctGainLoss = totalCost != 0 ? totalGainLoss / totalCost * 100m : 0;

            rows.Add(new InvestmentPerformanceRowViewModel
            {
                Portfolio    = "Total",
                CurrentValue = totalValue,
                CostBasis    = totalCost,
                IsTotal      = true,
                TotalGainLoss    = totalGainLoss,
                TotalPctGainLoss = totalPctGainLoss
            });

            _rows = rows;
            PerfGrid.ItemsSource = rows;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title      = "Export Investment Performance",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"InvestmentPerformance_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Portfolio,Name of Security,Symbol,Qty,Price,Current Value,Cost Basis,Gain/Loss,% Gain/Loss");
                foreach (var row in _rows)
                    sb.AppendLine(string.Join(",",
                        CsvEscape(row.Portfolio),
                        CsvEscape(row.Name),
                        CsvEscape(row.Symbol),
                        CsvEscape(row.QuantityExport),
                        CsvEscape(row.PriceExport),
                        CsvEscape(row.CurrentValueExport),
                        CsvEscape(row.CostBasisExport),
                        CsvEscape(row.GainLossExport),
                        CsvEscape(row.PctGainLossExport)));

                File.WriteAllText(saveDlg.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Investment Performance exported successfully:\n{saveDlg.FileName}",
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
    }

    public class InvestmentPerformanceRowViewModel
    {
        public string  Portfolio { get; set; } = "";
        public string  Name      { get; set; } = "";
        public string  Symbol    { get; set; } = "";
        public decimal Quantity  { get; set; }
        public decimal Price     { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal CostBasis    { get; set; }
        public bool    IsTotal      { get; set; }

        public decimal TotalGainLoss    { get; set; }
        public decimal TotalPctGainLoss { get; set; }

        private decimal GainLoss    => CurrentValue - CostBasis;
        private decimal PctGainLoss => CostBasis != 0 ? GainLoss / CostBasis * 100m : 0;

        public string RowFontWeight => IsTotal ? "Bold" : "Normal";

        public string QuantityDisplay      => IsTotal ? "" : Quantity.ToString("N4");
        public string PriceDisplay         => IsTotal ? "" : Price.ToString("N4");
        public string CurrentValueDisplay  => CurrentValue.ToString("N2");
        public string CostBasisDisplay     => CostBasis.ToString("N2");
        public string GainLossDisplay      => IsTotal ? TotalGainLoss.ToString("N2") : GainLoss.ToString("N2");
        public string PctGainLossDisplay   => IsTotal
                                                 ? TotalPctGainLoss.ToString("N2") + "%"
                                                 : PctGainLoss.ToString("N2") + "%";

        // CSV export uses these instead of the *_Display properties above so
        // large numbers don't come out with a comma thousands separator.
        public string QuantityExport      => IsTotal ? "" : Quantity.ToString("F4");
        public string PriceExport         => IsTotal ? "" : Price.ToString("F4");
        public string CurrentValueExport  => CurrentValue.ToString("F2");
        public string CostBasisExport     => CostBasis.ToString("F2");
        public string GainLossExport      => IsTotal ? TotalGainLoss.ToString("F2") : GainLoss.ToString("F2");
        public string PctGainLossExport   => IsTotal
                                                ? TotalPctGainLoss.ToString("F2") + "%"
                                                : PctGainLoss.ToString("F2") + "%";
    }
}
