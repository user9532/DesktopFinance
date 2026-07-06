using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class RecurringPaymentDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly List<string> _allCategories = new();

        public RecurringPaymentDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
            LoadAccounts();
            LoadCategories();
            DpNextDue.SelectedDate = DateTime.Today;
            CbFrequency.SelectedIndex = 3; // Monthly default
            AddNewLine(); // Start with one empty line
        }

        // ── data loading ──────────────────────────────────────────────────

        private void LoadAccounts()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountType = 'Bank' " +
                "ORDER BY AccountName;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                CbAccount.Items.Add(rdr.GetString(0));

            if (CbAccount.Items.Count > 0)
                CbAccount.SelectedIndex = 0;
        }

        private void LoadCategories()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName, AccountType FROM AccountChart " +
                "ORDER BY CASE AccountType " +
                "    WHEN 'Expense'   THEN 1 " +
                "    WHEN 'Income'    THEN 2 " +
                "    WHEN 'Asset'     THEN 3 " +
                "    WHEN 'Liability' THEN 4 " +
                "    ELSE 5 END, AccountName;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                _allCategories.Add(rdr.GetString(0));
        }

        // ── line management ───────────────────────────────────────────────

        private void AddNew_Click(object sender, RoutedEventArgs e) => AddNewLine();

        private void AddNewLine()
        {
            var line = BuildLine();
            LinesPanel.Children.Add(line);
        }

        private Grid BuildLine()
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            var txtName = new TextBox { Tag = "Name" };
            Grid.SetColumn(txtName, 0);

            var txtAmount = new TextBox { Tag = "Amount", HorizontalContentAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(txtAmount, 2);

            var txtMemo = new TextBox { Tag = "Memo" };
            Grid.SetColumn(txtMemo, 4);

            var cbCategory = new ComboBox { Tag = "Category" };
            foreach (var cat in _allCategories)
                cbCategory.Items.Add(cat);
            if (cbCategory.Items.Count > 0)
                cbCategory.SelectedIndex = 0;
            Grid.SetColumn(cbCategory, 6);

            var btnRemove = new Button
            {
                Content = "✕",
                Width   = 26,
                Height  = 22,
                ToolTip = "Remove line",
                Tag     = "Remove"
            };
            btnRemove.Click += (s, e) => LinesPanel.Children.Remove(grid);
            Grid.SetColumn(btnRemove, 8);

            grid.Children.Add(txtName);
            grid.Children.Add(txtAmount);
            grid.Children.Add(txtMemo);
            grid.Children.Add(cbCategory);
            grid.Children.Add(btnRemove);

            return grid;
        }

        // ── save ──────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (CbAccount.SelectedItem is not string account || account.Length == 0)
            {
                Warn("Please select a bank account.");
                return;
            }
            if (DpNextDue.SelectedDate is not DateTime nextDue)
            {
                Warn("Please select a next due date.");
                return;
            }
            if (CbFrequency.SelectedItem is not ComboBoxItem freqItem)
            {
                Warn("Please select a frequency.");
                return;
            }
            int frequency = int.Parse((string)freqItem.Tag);

            // Collect and validate lines
            var lines = new List<(string Name, decimal Amount, string Memo, string Category)>();
            foreach (Grid grid in LinesPanel.Children)
            {
                var name     = ((TextBox)FindTagged(grid, "Name")).Text.Trim();
                var amtText  = ((TextBox)FindTagged(grid, "Amount")).Text.Trim();
                var memo     = ((TextBox)FindTagged(grid, "Memo")).Text.Trim();
                var category = (string?)((ComboBox)FindTagged(grid, "Category")).SelectedItem ?? "";

                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(amtText))
                    continue; // skip fully blank lines

                if (string.IsNullOrEmpty(name))
                {
                    Warn("Each line requires a payee name.");
                    return;
                }
                if (!decimal.TryParse(amtText, out var amount) || amount == 0)
                {
                    Warn($"Amount for '{name}' must be a non-zero number.");
                    return;
                }
                if (category == account)
                {
                    Warn($"Category and Bank Account cannot be the same value ('{account}').");
                    return;
                }

                lines.Add((name, amount, memo, category));
            }

            if (lines.Count == 0)
            {
                Warn("Please add at least one payment line.");
                return;
            }

            string nextDueStr = nextDue.ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();

            // Determine the next Id: max existing Id + 1, or 1 if table is empty.
            long nextId;
            using (var idCmd = new SqliteCommand(
                "SELECT COALESCE(MAX(Id), 0) + 1 FROM RecurTrans;", conn))
            {
                nextId = (long)idCmd.ExecuteScalar()!;
            }

            foreach (var (name, amount, memo, category) in lines)
            {
                using var cmd = new SqliteCommand(
                    "INSERT INTO RecurTrans (Id, Name, Amount, Memo, Account, Category, Frequency, NextDue, LastPosted, TrType) " +
                    "VALUES (@id, @name, @amount, @memo, @account, @category, @frequency, @nextDue, NULL, 1);", conn);
                cmd.Parameters.AddWithValue("@id",        nextId);
                cmd.Parameters.AddWithValue("@name",      name);
                cmd.Parameters.AddWithValue("@amount",    (double)amount);
                cmd.Parameters.AddWithValue("@memo",      memo);
                cmd.Parameters.AddWithValue("@account",   account);
                cmd.Parameters.AddWithValue("@category",  category);
                cmd.Parameters.AddWithValue("@frequency", frequency);
                cmd.Parameters.AddWithValue("@nextDue",   nextDueStr);
                cmd.ExecuteNonQuery();
            }

            DialogResult = true;
        }

        // ── helpers ───────────────────────────────────────────────────────

        private static UIElement FindTagged(Grid grid, string tag)
        {
            foreach (UIElement child in grid.Children)
                if (child is FrameworkElement fe && (string?)fe.Tag == tag)
                    return child;
            throw new InvalidOperationException($"Tag '{tag}' not found in line grid.");
        }

        private static void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
