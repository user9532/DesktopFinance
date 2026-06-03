using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class NewCategoryDialog : Window
    {
        private readonly DatabaseManager _db;

        public NewCategoryDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var categoryName = TxtCategoryName.Text.Trim();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                MessageBox.Show("Category name is required.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCategoryName.Focus();
                return;
            }

            if (_db.AccountExists(categoryName))
            {
                MessageBox.Show($"Category '{categoryName}' already exists.",
                    "Category Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string categoryType = RbExpense.IsChecked is true ? "Expense" : "Income";

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "INSERT INTO AccountChart (AccountName, AccountType, AccountGroup) " +
                "VALUES (@name, @type, @grp);", conn);

            cmd.Parameters.AddWithValue("@name", categoryName);
            cmd.Parameters.AddWithValue("@type", categoryType);
            cmd.Parameters.AddWithValue("@grp",  categoryType);
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
