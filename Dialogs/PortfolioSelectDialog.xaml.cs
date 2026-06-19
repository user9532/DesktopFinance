using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class PortfolioSelectDialog : Window
    {
        public string? SelectedPortfolio { get; private set; }

        public PortfolioSelectDialog(DatabaseManager db)
        {
            InitializeComponent();
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT DISTINCT Portfolio FROM AccountChart WHERE Portfolio IS NOT NULL AND Portfolio != '' ORDER BY Portfolio;",
                conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbPortfolio.Items.Add(r.GetString(0));

            if (CbPortfolio.Items.Count > 0)
                CbPortfolio.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (CbPortfolio.SelectedItem == null)
            {
                MessageBox.Show("Please select a portfolio.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedPortfolio = CbPortfolio.SelectedItem.ToString();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
