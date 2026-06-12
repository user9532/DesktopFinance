using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class AddQuoteDialog : Window
    {
        private readonly DatabaseManager _db;

        public AddQuoteDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
            LoadSymbols();
        }

        private void LoadSymbols()
        {
            CbSymbol.Items.Clear();
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Symbol FROM Securities ORDER BY Symbol;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbSymbol.Items.Add(r.GetString(0));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (CbSymbol.SelectedItem == null)
            {
                MessageBox.Show("Please select a symbol.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CbSymbol.Focus();
                return;
            }
            if (DpDate.SelectedDate == null)
            {
                MessageBox.Show("Please select a date.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                DpDate.Focus();
                return;
            }
            if (!decimal.TryParse(TxtPrice.Text.Trim(), out var price))
            {
                MessageBox.Show("Please enter a valid numeric price.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPrice.Focus();
                return;
            }

            InsertQuote(_db, DpDate.SelectedDate.Value.ToString("yyyy-MM-dd"),
                        (string)CbSymbol.SelectedItem, price);

            MessageBox.Show("Quote saved.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Clear fields for another entry
            DpDate.SelectedDate = null;
            TxtPrice.Text = "";
            CbSymbol.SelectedItem = null;
        }

        private void ImportHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select Quote History CSV",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = File.ReadAllLines(dlg.FileName)
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToArray();

                if (lines.Length < 2)
                {
                    MessageBox.Show("The file must contain a header row and at least one data row.",
                        "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Parse header — case-insensitive, trim whitespace
                var headers = SplitCsvLine(lines[0]);
                int dateIdx   = FindHeader(headers, "date");
                int symbolIdx = FindHeader(headers, "symbol");
                int priceIdx  = FindHeader(headers, "price");

                if (dateIdx < 0 || symbolIdx < 0 || priceIdx < 0)
                {
                    var missing = new List<string>();
                    if (dateIdx   < 0) missing.Add("Date");
                    if (symbolIdx < 0) missing.Add("Symbol");
                    if (priceIdx  < 0) missing.Add("Price");
                    MessageBox.Show(
                        $"The following required column(s) were not found in the header:\n\n" +
                        string.Join(", ", missing) +
                        "\n\nExpected columns: Date, Symbol, Price.",
                        "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int imported = 0, skipped = 0;

                using var conn = _db.OpenConnection();
                using var tx   = conn.BeginTransaction();

                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = SplitCsvLine(lines[i]);
                    if (cols.Length <= Math.Max(dateIdx, Math.Max(symbolIdx, priceIdx)))
                    {
                        skipped++;
                        continue;
                    }

                    var dateStr  = cols[dateIdx].Trim();
                    var symbol   = cols[symbolIdx].Trim();
                    var priceStr = cols[priceIdx].Trim();

                    if (!DateTime.TryParse(dateStr, out var dt) ||
                        string.IsNullOrEmpty(symbol) ||
                        !decimal.TryParse(priceStr, out var prc))
                    {
                        skipped++;
                        continue;
                    }

                    InsertQuote(conn, tx, dt.ToString("yyyy-MM-dd"), symbol, prc);
                    imported++;
                }

                tx.Commit();

                var msg = $"Import complete.\n\nRows imported: {imported}";
                if (skipped > 0) msg += $"\nRows skipped (invalid): {skipped}";
                MessageBox.Show(msg, "Import Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n\n{ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // ── helpers ──────────────────────────────────────────────────────────

        private void InsertQuote(DatabaseManager db, string date, string symbol, decimal price)
        {
            using var conn = db.OpenConnection();
            InsertQuote(conn, null, date, symbol, price);
        }

        private static void InsertQuote(SqliteConnection conn,
            SqliteTransaction? tx, string date, string symbol, decimal price)
        {
            using var cmd = new SqliteCommand(
                "INSERT OR REPLACE INTO Quotes (Date, Symbol, Price) VALUES (@dt, @sym, @prc);", conn, tx);
            cmd.Parameters.AddWithValue("@dt",  date);
            cmd.Parameters.AddWithValue("@sym", symbol);
            cmd.Parameters.AddWithValue("@prc", (double)price);
            cmd.ExecuteNonQuery();
        }

        private static int FindHeader(string[] headers, string name) =>
            Array.FindIndex(headers, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

        // Handles quoted fields containing commas.
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
