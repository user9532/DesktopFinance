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
                "SELECT AccountName, AccountGroup FROM AccountChart " +
                "WHERE AccountGroup IN ('Expense','Income') " +
                "ORDER BY AccountName;", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                LbCategories.Items.Add(new CategoryRow
                {
                    Name  = reader.GetString(0),
                    Group = reader.IsDBNull(1) ? "" : reader.GetString(1)
                });
        }

        private bool TryCommit()
        {
            if (LbCategories.SelectedItem is CategoryRow row)
            {
                SelectedCategory = row.Name;
                DialogResult = true;
                return true;
            }
            return false;
        }

        private void Select_Click(object sender, RoutedEventArgs e) => TryCommit();

        private void LbCategories_DoubleClick(object sender, MouseButtonEventArgs e) => TryCommit();

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private class CategoryRow
        {
            public string Name  { get; set; } = "";
            public string Group { get; set; } = "";
        }
    }
}
