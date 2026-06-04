using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class EditAccountDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly string _originalName;

        public EditAccountDialog(DatabaseManager db, string accountName)
        {
            InitializeComponent();
            _db           = db;
            _originalName = accountName;
            TxtName.Text  = accountName;
            TxtName.SelectAll();
            TxtName.Focus();
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
            using var cmd = new SqliteCommand(
                "UPDATE AccountChart SET AccountName = @new WHERE AccountName = @old;", conn);
            cmd.Parameters.AddWithValue("@new", newName);
            cmd.Parameters.AddWithValue("@old", _originalName);
            cmd.ExecuteNonQuery();

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
