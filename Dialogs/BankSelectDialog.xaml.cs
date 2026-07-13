using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class BankSelectDialog : Window
    {
        public string SelectedAccount { get; private set; } = "";

        public BankSelectDialog(DatabaseManager db)
        {
            InitializeComponent();
            LoadAccounts(db);
        }

        private void LoadAccounts(DatabaseManager db)
        {
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountType = 'Bank' " +
                "  AND AccountCode = 1 " +
                "ORDER BY AccountName;", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                CbAccount.Items.Add(reader.GetString(0));

            if (CbAccount.Items.Count > 0)
                CbAccount.SelectedIndex = 0;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (CbAccount.SelectedItem is string s && s.Length > 0)
            {
                SelectedAccount = s;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
