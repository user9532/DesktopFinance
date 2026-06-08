using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Input;

namespace FinanceApp.Dialogs
{
    public partial class EditAccountSelectDialog : Window
    {
        public string SelectedAccount { get; private set; } = "";

        public EditAccountSelectDialog(DatabaseManager db)
        {
            InitializeComponent();
            LoadAccounts(db);
        }

        private void LoadAccounts(DatabaseManager db)
        {
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountGroup IN ('Asset','Liability') " +
                "ORDER BY CASE AccountType " +
                "  WHEN 'Bank'        THEN 1 " +
                "  WHEN 'Credit Card' THEN 2 " +
                "  WHEN 'Investment'  THEN 3 " +
                "  WHEN 'Asset'       THEN 4 " +
                "  WHEN 'Loan'        THEN 5 " +
                "  ELSE 6 END, " +
                "AccountName;", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                LbAccounts.Items.Add(reader.GetString(0));
        }

        private bool TryCommit()
        {
            if (LbAccounts.SelectedItem is string s && s.Length > 0)
            {
                SelectedAccount = s;
                DialogResult = true;
                return true;
            }
            return false;
        }

        private void Select_Click(object sender, RoutedEventArgs e) => TryCommit();

        private void LbAccounts_DoubleClick(object sender, MouseButtonEventArgs e) => TryCommit();

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
