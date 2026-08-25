using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class EditAccountDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _originalName;
        private readonly bool _inTransactions;

        public EditAccountDialog(DatabaseManager db, string accountName, bool inTransactions)
        {
            InitializeComponent();
            _db             = db;
            _originalName   = accountName;
            _inTransactions = inTransactions;
            TxtName.Text    = accountName;
            TxtName.SelectAll();
            TxtName.Focus();

            // Deleting would leave transactions/history referencing a non-existent
            // account, so only offer it when the account isn't in use anywhere.
            BtnDelete.Visibility = inTransactions ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var newName = TxtName.Text.Trim();

            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Account name cannot be blank.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtName.Focus();
                return;
            }

            if (newName == _originalName)
            {
                DialogResult = false;
                return;
            }

            // Prevent duplicate names
            if (AccountExists(newName))
            {
                MessageBox.Show($"An account named '{newName}' already exists.",
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
            Rename("AccountMap",   "AccountName");
            Rename("RecurTrans",   "Account");
            Rename("RecurTrans",   "Category");
            Rename("LoanData",     "AccountName");
            Rename("AccountChart", "AccountName");

            tx.Commit();

            MessageBox.Show(
                $"Account '{_originalName}' was renamed to '{newName}' and all related records were updated successfully.",
                "Account Updated", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete the account '{_originalName}'?\n\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "DELETE FROM AccountChart WHERE AccountName = @name;", conn);
            cmd.Parameters.AddWithValue("@name", _originalName);
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }

        private bool AccountExists(string name)
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
