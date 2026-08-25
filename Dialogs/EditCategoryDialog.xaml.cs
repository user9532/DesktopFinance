using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class EditCategoryDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _originalName;
        private readonly bool _inTransactions;

        public EditCategoryDialog(DatabaseManager db, string categoryName, bool inTransactions)
        {
            InitializeComponent();
            _db             = db;
            _originalName   = categoryName;
            _inTransactions = inTransactions;
            TxtName.Text    = categoryName;
            TxtGroup.Text   = LoadAccountGroup(categoryName);
            TxtName.SelectAll();
            TxtName.Focus();

            // Deleting would leave transactions/history referencing a non-existent
            // category, so only offer it when the category isn't in use anywhere.
            BtnDelete.Visibility = inTransactions ? Visibility.Collapsed : Visibility.Visible;
        }

        private string LoadAccountGroup(string categoryName)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountGroup FROM AccountChart WHERE AccountName = @name;", conn);
            cmd.Parameters.AddWithValue("@name", categoryName);
            return cmd.ExecuteScalar() as string ?? "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var newName = TxtName.Text.Trim();

            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Category name cannot be blank.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            if (newName == _originalName)
            {
                DialogResult = false;
                return;
            }

            if (CategoryExists(newName))
            {
                MessageBox.Show($"A category named '{newName}' already exists.",
                    "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            using var conn = _db.OpenConnection();
            using var tx   = conn.BeginTransaction();

            void Rename(string table, string column)
            {
                using var cmd = new SqliteCommand(
                    $"UPDATE {table} SET {column} = @new WHERE {column} = @old;", conn, tx);
                cmd.Parameters.AddWithValue("@new", newName);
                cmd.Parameters.AddWithValue("@old", _originalName);
                cmd.ExecuteNonQuery();
            }

            Rename("Transactions", "Account");
            Rename("Transactions", "Category");
            Rename("CatHistory",   "Category");
            Rename("RecurTrans",   "Account");
            Rename("RecurTrans",   "Category");
            Rename("Budget",       "Category");
            Rename("AccountChart", "AccountName");

            tx.Commit();

            MessageBox.Show(
                $"Category '{_originalName}' was renamed to '{newName}' and all related records were updated successfully.",
                "Category Updated", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete the category '{_originalName}'?\n\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "DELETE FROM AccountChart WHERE AccountName = @name;", conn);
            cmd.Parameters.AddWithValue("@name", _originalName);
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }

        private bool CategoryExists(string name)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM AccountChart WHERE AccountName = @name;", conn);
            cmd.Parameters.AddWithValue("@name", name);
            return (long)cmd.ExecuteScalar()! > 0;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
