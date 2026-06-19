using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class StockSplitDialog : Window
    {
        private readonly DatabaseManager _db;

        public StockSplitDialog(DatabaseManager db)
        {
            _db = db;
            InitializeComponent();
            LoadSymbols();
            DpSplitDate.SelectedDate = DateTime.Today;
        }

        private void LoadSymbols()
        {
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
                MessageBox.Show("Please select a stock symbol.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DpSplitDate.SelectedDate == null)
            {
                MessageBox.Show("Please enter the date of the split.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(TbX.Text, out var x) || x <= 0 ||
                !decimal.TryParse(TbY.Text, out var y) || y <= 0)
            {
                MessageBox.Show("Please enter valid positive numbers for the split ratio.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var symbol     = CbSymbol.SelectedItem.ToString()!;
            var splitDate  = DpSplitDate.SelectedDate.Value.ToString("yyyy-MM-dd");
            var splitRatio = (double)(x / y);

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                UPDATE Securities
                SET    IsSplit = 1, SplitDate = @date, SplitRatio = @ratio
                WHERE  Symbol = @sym;", conn);
            cmd.Parameters.AddWithValue("@sym",   symbol);
            cmd.Parameters.AddWithValue("@date",  splitDate);
            cmd.Parameters.AddWithValue("@ratio", splitRatio);
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }
    }
}
