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
    public partial class DrilldownCategoryView : UserControl
    {
        private readonly string _category;
        private readonly DateTime _startDate;
        private readonly DateTime _endDate;
        private List<DrillRow> _rows = new();

        public DrilldownCategoryView(DatabaseManager db, string category, DateTime startDate, DateTime endDate)
        {
            InitializeComponent();
            _category     = category;
            _startDate    = startDate;
            _endDate      = endDate;
            TbHeader.Text = $"Category: {category}   ({startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy})";
            LoadData(db);
        }

        private void LoadData(DatabaseManager db)
        {
            _rows = new List<DrillRow>();

            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT Date, CheckNo, Account, Name, Clear, Amount, Memo, TrnType
                FROM   Transactions
                WHERE  Category = @cat AND Date >= @start AND Date <= @end
                ORDER  BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@cat",   _category);
            cmd.Parameters.AddWithValue("@start", _startDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@end",   _endDate.ToString("yyyy-MM-dd"));

            decimal balance = 0m;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var trnType = r.IsDBNull(7) ? "" : r.GetString(7);
                var amount  = r.IsDBNull(5) ? 0m : (decimal)r.GetDouble(5);

                // CREDIT → show in Debit column; DEBIT → show in Credit column
                decimal debit  = trnType == "CREDIT" ? amount : 0m;
                decimal credit = trnType == "DEBIT"  ? amount : 0m;

                balance += debit - credit;

                _rows.Add(new DrillRow
                {
                    DateDisplay = r.IsDBNull(0) ? "" :
                        DateTime.TryParse(r.GetString(0), out var d) ? d.ToString("MM/dd/yyyy") : r.GetString(0),
                    CheckNo  = r.IsDBNull(1) ? "" : r.GetString(1),
                    Account  = r.IsDBNull(2) ? "" : r.GetString(2),
                    Name     = r.IsDBNull(3) ? "" : r.GetString(3),
                    Clear    = !r.IsDBNull(4) && r.GetInt32(4) != 0,
                    Debit    = debit,
                    Credit   = credit,
                    Balance  = balance,
                    Memo     = r.IsDBNull(6) ? "" : r.GetString(6),
                });
            }

            TrGrid.ItemsSource = _rows;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export Drilldown as CSV",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"Drilldown_{_category}.csv",
                DefaultExt = ".csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Category Drilldown: {CsvEscape(_category)}");
                sb.AppendLine("Date,Check No,Account,Name,Clear,Debit,Credit,Balance,Memo");
                foreach (var row in _rows)
                {
                    sb.AppendLine(
                        $"{row.DateDisplay}," +
                        $"{CsvEscape(row.CheckNo)}," +
                        $"{CsvEscape(row.Account)}," +
                        $"{CsvEscape(row.Name)}," +
                        $"{(row.Clear ? "Y" : "")}," +
                        $"{row.DebitDisplay}," +
                        $"{row.CreditDisplay}," +
                        $"{row.BalanceDisplay}," +
                        $"{CsvEscape(row.Memo)}");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Exported to:\n{dlg.FileName}",
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

        private class DrillRow
        {
            public string  DateDisplay   { get; set; } = "";
            public string  CheckNo       { get; set; } = "";
            public string  Account       { get; set; } = "";
            public string  Name          { get; set; } = "";
            public bool    Clear         { get; set; }
            public decimal Debit         { get; set; }
            public decimal Credit        { get; set; }
            public decimal Balance       { get; set; }
            public string  Memo          { get; set; } = "";

            public string DebitDisplay   => Debit  != 0 ? Debit.ToString("N2")   : "";
            public string CreditDisplay  => Credit != 0 ? Credit.ToString("N2")  : "";
            public string BalanceDisplay => Balance.ToString("N2");
        }
    }
}
