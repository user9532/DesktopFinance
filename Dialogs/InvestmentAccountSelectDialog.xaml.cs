using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class InvestmentAccountSelectDialog : Window
    {
        public string? SelectedAccount { get; private set; }

        public InvestmentAccountSelectDialog(DatabaseManager db)
        {
            InitializeComponent();
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE AccountCode = 2 ORDER BY AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbAccount.Items.Add(r.GetString(0));

            if (CbAccount.Items.Count > 0)
                CbAccount.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (CbAccount.SelectedItem == null)
            {
                MessageBox.Show("Please select an account.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedAccount = (string)CbAccount.SelectedItem;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
