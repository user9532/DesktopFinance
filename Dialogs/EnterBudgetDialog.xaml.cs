using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class EnterBudgetDialog : Window
    {
        private readonly DatabaseManager _db;
        private TextBox[] _months = Array.Empty<TextBox>();
        private bool _ready;   // suppresses LoadData during initialization

        public EnterBudgetDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db     = db;
            _months = new[] { M01, M02, M03, M04, M05, M06, M07, M08, M09, M10, M11, M12 };
            M01.TextChanged += M01_TextChanged;

            LoadYears();
            LoadCategories(); // sets _ready = true and calls LoadData at the end
        }

        // ── initialization ────────────────────────────────────────────────

        private void LoadYears()
        {
            var now = DateTime.Today.Year;
            for (int y = now - 5; y <= now + 5; y++)
                CbYear.Items.Add(y);
            CbYear.SelectedItem = now;          // fires SelectionChanged, but _ready is false
        }

        private void LoadCategories()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT AccountName FROM AccountChart
                WHERE AccountGroup IN ('Expense','Income')
                ORDER BY
                    CASE AccountGroup WHEN 'Expense' THEN 1 ELSE 2 END,
                    AccountName;", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                CbCategory.Items.Add(reader.GetString(0));

            _ready = true;

            if (CbCategory.Items.Count > 0)
                CbCategory.SelectedIndex = 0;   // fires SelectionChanged → LoadData
            else
                LoadData();                      // nothing selected but clear boxes anyway
        }

        // ── data loading ──────────────────────────────────────────────────

        private void CbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) LoadData();
        }

        private void CbYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) LoadData();
        }

        private void LoadData()
        {
            foreach (var box in _months) box.Text = "";
            ChkUseJanForAll.IsChecked  = false;
            ChkUseJanForAll.IsEnabled  = false;

            if (CbCategory.SelectedItem is not string category) return;
            if (CbYear.SelectedItem is not int year) return;

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Date, Budget FROM Budget " +
                "WHERE Category = @cat AND Date LIKE @year;", conn);
            cmd.Parameters.AddWithValue("@cat",  category);
            cmd.Parameters.AddWithValue("@year", $"{year}-%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!DateTime.TryParse(reader.GetString(0), out var dt)) continue;
                var idx = dt.Month - 1;
                if (idx < 0 || idx > 11) continue;
                var amt = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                if (amt != 0)
                    _months[idx].Text = ((decimal)amt).ToString("N2");
            }
        }

        // ── January-for-all-months ──────────────────────────────────────────

        private void M01_TextChanged(object sender, TextChangedEventArgs e)
        {
            ChkUseJanForAll.IsEnabled = !string.IsNullOrWhiteSpace(M01.Text);
            if (!ChkUseJanForAll.IsEnabled)
                ChkUseJanForAll.IsChecked = false;
        }

        private void ChkUseJanForAll_Checked(object sender, RoutedEventArgs e)
        {
            for (int i = 1; i < _months.Length; i++)
                _months[i].Text = M01.Text;
        }

        // ── save ──────────────────────────────────────────────────────────

        private void SaveData()
        {
            if (CbCategory.SelectedItem is not string category) return;
            if (CbYear.SelectedItem is not int year) return;

            using var conn = _db.OpenConnection();

            for (int m = 1; m <= 12; m++)
            {
                var text = _months[m - 1].Text.Trim();
                var amt  = decimal.TryParse(text, out var d) ? d : 0m;
                var date = $"{year}-{m:00}-01";
                var like = $"{year}-{m:00}%";

                using var chk = new SqliteCommand(
                    "SELECT COUNT(*) FROM Budget " +
                    "WHERE Category = @cat AND Date LIKE @like;", conn);
                chk.Parameters.AddWithValue("@cat",  category);
                chk.Parameters.AddWithValue("@like", like);
                var exists = (long)chk.ExecuteScalar()! > 0;

                if (exists)
                {
                    using var upd = new SqliteCommand(
                        "UPDATE Budget SET Budget = @amt " +
                        "WHERE Category = @cat AND Date LIKE @like;", conn);
                    upd.Parameters.AddWithValue("@amt",  (double)amt);
                    upd.Parameters.AddWithValue("@cat",  category);
                    upd.Parameters.AddWithValue("@like", like);
                    upd.ExecuteNonQuery();
                }
                else
                {
                    using var ins = new SqliteCommand(
                        "INSERT INTO Budget (Date, Category, Budget) " +
                        "VALUES (@date, @cat, @amt);", conn);
                    ins.Parameters.AddWithValue("@date", date);
                    ins.Parameters.AddWithValue("@cat",  category);
                    ins.Parameters.AddWithValue("@amt",  (double)amt);
                    ins.ExecuteNonQuery();
                }
            }
        }

        // After save, advance to the next category so the user can enter the next one.
        private void MoveToNext()
        {
            var next = CbCategory.SelectedIndex + 1;
            if (next < CbCategory.Items.Count)
                CbCategory.SelectedIndex = next;   // fires SelectionChanged → LoadData
        }

        // ── button handlers ───────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveData();
            MoveToNext();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SaveData();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
