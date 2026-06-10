using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class AddSecurityDialog : Window
    {
        private readonly DatabaseManager _db;

        public AddSecurityDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var symbol = TxtSymbol.Text.Trim();
            var description = TxtDescription.Text.Trim();

            if (string.IsNullOrEmpty(symbol))
            {
                MessageBox.Show("Ticker Symbol is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSymbol.Focus();
                return;
            }

            using var conn = _db.OpenConnection();

            // Check for duplicate symbol
            using var checkCmd = new SqliteCommand(
                "SELECT COUNT(*) FROM Securities WHERE Symbol = @sym;", conn);
            checkCmd.Parameters.AddWithValue("@sym", symbol);
            if ((long)checkCmd.ExecuteScalar()! > 0)
            {
                MessageBox.Show($"Ticker symbol \"{symbol}\" already exists in the Securities table.",
                    "Duplicate Symbol", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtSymbol.Focus();
                return;
            }

            using var insertCmd = new SqliteCommand(
                "INSERT INTO Securities (Symbol, Description) VALUES (@sym, @desc);", conn);
            insertCmd.Parameters.AddWithValue("@sym", symbol);
            insertCmd.Parameters.AddWithValue("@desc", description);
            insertCmd.ExecuteNonQuery();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
