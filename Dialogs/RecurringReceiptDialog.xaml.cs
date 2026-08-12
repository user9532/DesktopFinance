using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    public partial class RecurringReceiptDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly List<string> _allCategories = new();
        private readonly long? _editId;

        public RecurringReceiptDialog(DatabaseManager db, long? editId = null)
        {
            InitializeComponent();
            _db = db;
            _editId = editId;
            LoadAccounts();
            LoadCategories();

            if (_editId is long id)
            {
                Title = "Edit Recurring Receipt";
                BtnDelete.Visibility = Visibility.Visible;
                LoadExisting(id);
            }
            else
            {
                DpNextDue.SelectedDate = DateTime.Today;
                CbFrequency.SelectedIndex = 3; // Monthly default
                AddNewLine();
            }
        }

        private void LoadExisting(long id)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Name, Account, Amount, Category, Memo, Frequency, NextDue " +
                "FROM RecurTrans WHERE Id = @id ORDER BY rowid;", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var rdr = cmd.ExecuteReader();

            bool first = true;
            while (rdr.Read())
            {
                var name     = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                var account  = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                var amount   = rdr.IsDBNull(2) ? 0.0 : rdr.GetDouble(2);
                var category = rdr.IsDBNull(3) ? "" : rdr.GetString(3);
                var memo     = rdr.IsDBNull(4) ? "" : rdr.GetString(4);
                var freq     = rdr.IsDBNull(5) ? 30 : rdr.GetInt32(5);
                var nextDue  = rdr.IsDBNull(6) ? "" : rdr.GetString(6);

                if (first)
                {
                    CbAccount.SelectedItem = account;
                    if (CbAccount.SelectedItem == null)
                        CbAccount.Text = account;

                    if (DateTime.TryParse(nextDue, out var nd))
                        DpNextDue.SelectedDate = nd;
                    else
                        DpNextDue.SelectedDate = DateTime.Today;

                    foreach (ComboBoxItem item in CbFrequency.Items)
                        if ((string)item.Tag == freq.ToString())
                        {
                            CbFrequency.SelectedItem = item;
                            break;
                        }

                    first = false;
                }

                var line = BuildLine();
                ((TextBox)FindTagged(line, "Name")).Text     = name;
                ((TextBox)FindTagged(line, "Amount")).Text   = amount.ToString("N2");
                ((TextBox)FindTagged(line, "Memo")).Text     = memo;
                var cbCategory = (ComboBox)FindTagged(line, "Category");
                cbCategory.SelectedItem = category;
                if (cbCategory.SelectedItem == null)
                    cbCategory.Text = category;
                LinesPanel.Children.Add(line);
            }

            if (LinesPanel.Children.Count == 0)
                AddNewLine();
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
                "SELECT AccountName FROM AccountChart " +
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
            LinesPanel.Children.Add(BuildLine());
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

            var lines = new List<(string Name, decimal Amount, string Memo, string Category)>();
            foreach (Grid grid in LinesPanel.Children)
            {
                var name     = ((TextBox)FindTagged(grid, "Name")).Text.Trim();
                var amtText  = ((TextBox)FindTagged(grid, "Amount")).Text.Trim();
                var memo     = ((TextBox)FindTagged(grid, "Memo")).Text.Trim();
                var category = (string?)((ComboBox)FindTagged(grid, "Category")).SelectedItem ?? "";

                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(amtText))
                    continue;

                if (string.IsNullOrEmpty(name))
                {
                    Warn("Each line requires a payor name.");
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
                Warn("Please add at least one receipt line.");
                return;
            }

            string nextDueStr = nextDue.ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();

            long nextId;
            string? lastPosted = null;
            if (_editId is long editId)
            {
                nextId = editId;
                using (var lpCmd = new SqliteCommand(
                    "SELECT LastPosted FROM RecurTrans WHERE Id = @id LIMIT 1;", conn))
                {
                    lpCmd.Parameters.AddWithValue("@id", editId);
                    var val = lpCmd.ExecuteScalar();
                    lastPosted = (val == null || val == DBNull.Value) ? null : (string)val;
                }
                using var delCmd = new SqliteCommand(
                    "DELETE FROM RecurTrans WHERE Id = @id;", conn);
                delCmd.Parameters.AddWithValue("@id", editId);
                delCmd.ExecuteNonQuery();
            }
            else
            {
                using var idCmd = new SqliteCommand(
                    "SELECT COALESCE(MAX(Id), 0) + 1 FROM RecurTrans;", conn);
                nextId = (long)idCmd.ExecuteScalar()!;
            }

            foreach (var (name, amount, memo, category) in lines)
            {
                using var cmd = new SqliteCommand(
                    "INSERT INTO RecurTrans (Id, Name, Amount, Memo, Account, Category, Frequency, NextDue, LastPosted, TrType) " +
                    "VALUES (@id, @name, @amount, @memo, @account, @category, @frequency, @nextDue, @lastPosted, 2);", conn);
                cmd.Parameters.AddWithValue("@id",        nextId);
                cmd.Parameters.AddWithValue("@lastPosted", (object?)lastPosted ?? DBNull.Value);
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

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_editId is not long editId) return;

            var confirm = MessageBox.Show(
                "Are you sure you want to delete this recurring transaction?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            using var conn = _db.OpenConnection();
            using var cmd  = new SqliteCommand(
                "DELETE FROM RecurTrans WHERE Id = @id;", conn);
            cmd.Parameters.AddWithValue("@id", editId);
            cmd.ExecuteNonQuery();

            DialogResult = true;
        }
    }
}
