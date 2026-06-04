using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Input;

namespace FinanceApp.Dialogs
{
    public partial class EditCategorySelectDialog : Window
    {
        public string SelectedCategory { get; private set; } = "";

        public EditCategorySelectDialog(DatabaseManager db)
        {
            InitializeComponent();
            LoadCategories(db);
        }

        private void LoadCategories(DatabaseManager db)
        {
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountGroup IN ('Expense','Income') " +
                "ORDER BY AccountName;", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                LbCategories.Items.Add(reader.GetString(0));
        }

        private bool TryCommit()
        {
            if (LbCategories.SelectedItem is string s && s.Length > 0)
            {
                SelectedCategory = s;
                DialogResult = true;
                return true;
            }
            return false;
        }

        private void Select_Click(object sender, RoutedEventArgs e) => TryCommit();

        private void LbCategories_DoubleClick(object sender, MouseButtonEventArgs e) => TryCommit();

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
