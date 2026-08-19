using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class DrilldownCategoryDialog : Window
    {
        public string   SelectedCategory { get; private set; } = "";
        public DateTime StartDate        { get; private set; }
        public DateTime EndDate          { get; private set; }

        public DrilldownCategoryDialog(DatabaseManager db)
        {
            InitializeComponent();
            LoadCategories(db);
            CbCategory.SelectionChanged += (s, e) => Validate();
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

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => Validate();

        private void Validate()
        {
            if (TbError == null || BtnContinue == null) return;   // called before InitializeComponent finishes

            var start = DpStart.SelectedDate;
            var end   = DpEnd.SelectedDate;

            if (CbCategory.SelectedItem == null || start == null || end == null)
            {
                TbError.Visibility = Visibility.Collapsed;
                BtnContinue.IsEnabled = false;
                return;
            }

            if (end <= start)
            {
                TbError.Text       = "End date must be later than start date.";
                TbError.Visibility = Visibility.Visible;
                BtnContinue.IsEnabled = false;
                return;
            }

            TbError.Visibility = Visibility.Collapsed;
            BtnContinue.IsEnabled = true;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            if (CbCategory.SelectedItem is not string s) return;
            if (DpStart.SelectedDate is not DateTime start) return;
            if (DpEnd.SelectedDate is not DateTime end) return;

            SelectedCategory = s;
            StartDate        = start;
            EndDate           = end;
            DialogResult      = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
