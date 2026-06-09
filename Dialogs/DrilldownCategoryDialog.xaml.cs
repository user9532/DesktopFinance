using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class DrilldownCategoryDialog : Window
    {
        public string SelectedCategory { get; private set; } = "";

        public DrilldownCategoryDialog(DatabaseManager db)
        {
            InitializeComponent();
            LoadCategories(db);
            CbCategory.SelectionChanged += (s, e) =>
                BtnContinue.IsEnabled = CbCategory.SelectedItem != null;
        }

        private void LoadCategories(DatabaseManager db)
        {
            using var conn = db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT AccountName FROM AccountChart
                WHERE AccountGroup IN ('Expense', 'Income')
                ORDER BY
                    CASE AccountGroup WHEN 'Expense' THEN 1 ELSE 2 END,
                    AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                CbCategory.Items.Add(r.GetString(0));
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            if (CbCategory.SelectedItem is string s)
            {
                SelectedCategory = s;
                DialogResult = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
