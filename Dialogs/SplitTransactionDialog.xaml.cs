using FinanceApp.Data;
using FinanceApp.Views;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FinanceApp.Dialogs
{
    public partial class SplitTransactionDialog : Window
    {
        private readonly DatabaseManager _db;
        private readonly TransactionRowViewModel _row;
        private readonly List<string> _categories;
        private readonly string _account;
        private readonly decimal _originalAmount;

        private readonly ObservableCollection<SplitLineViewModel> _splitLines = new();

        public SplitTransactionDialog(
            DatabaseManager db,
            TransactionRowViewModel row,
            List<string> categories,
            string account)
        {
            InitializeComponent();
            _db             = db;
            _row            = row;
            _categories     = categories;
            _account        = account;
            _originalAmount = row.PaymentDecimal > 0 ? row.PaymentDecimal : row.DepositDecimal;

            PopulateHeader();
            SeedSplitLines();

            SplitGrid.ItemsSource = _splitLines;
            UpdateTotals();
        }

        // ── header ────────────────────────────────────────────────────────

        private void PopulateHeader()
        {
            TxDate.Text     = _row.DateDisplay;
            TxName.Text     = _row.Name;
            TxAmount.Text   = _originalAmount.ToString("N2");
            TxCategory.Text = _row.DisplayCategory;
            TxMemo.Text     = _row.Memo;
            TxAmtLabel.Text = _row.PaymentDecimal > 0 ? "Payment:" : "Deposit:";
        }

        // Start with two rows — first inherits the original category
        private void SeedSplitLines()
        {
            _splitLines.Add(new SplitLineViewModel
            {
                Category = _row.DisplayCategory,
                Memo     = _row.Memo
            });
            _splitLines.Add(new SplitLineViewModel());
        }

        // ── split-line events ─────────────────────────────────────────────

        private void AddSplit_Click(object sender, RoutedEventArgs e)
        {
            _splitLines.Add(new SplitLineViewModel());
            UpdateTotals();
        }

        private void RemoveSplit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SplitLineViewModel line)
            {
                if (_splitLines.Count <= 2)
                {
                    MessageBox.Show("A split requires at least two lines.",
                        "Cannot Remove", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _splitLines.Remove(line);
                UpdateTotals();
            }
        }

        private void SplitGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit)
                Dispatcher.BeginInvoke(new Action(UpdateTotals));
        }

        private void SplitCategoryCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb)
            {
                cb.ItemsSource    = _categories;
                cb.IsDropDownOpen = true;
            }
        }

        // ── running totals ────────────────────────────────────────────────

        private void UpdateTotals()
        {
            var total     = _splitLines.Sum(l => l.AmountDecimal);
            var remaining = _originalAmount - total;
            TxTotal.Text     = total.ToString("N2");
            TxRemaining.Text = remaining.ToString("N2");
            TxRemaining.Foreground = Math.Abs(remaining) < 0.005m
                ? Brushes.DarkGreen
                : Brushes.Red;
        }

        // ── save ──────────────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_splitLines.Any(l => l.AmountDecimal <= 0))
            {
                MessageBox.Show("All split amounts must be greater than zero.",
                    "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var total = _splitLines.Sum(l => l.AmountDecimal);
            if (Math.Abs(total - _originalAmount) >= 0.005m)
            {
                MessageBox.Show(
                    $"Split total ({total:N2}) must equal the original amount ({_originalAmount:N2}).\n" +
                    $"Remaining: {(_originalAmount - total):N2}",
                    "Amount Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var conn = _db.OpenConnection();

                // Remove the original record
                using (var del = new SqliteCommand(
                    "DELETE FROM Transactions WHERE rowid = @id;", conn))
                {
                    del.Parameters.AddWithValue("@id", _row.RowId);
                    del.ExecuteNonQuery();
                }

                // Insert one new record per split line
                foreach (var line in _splitLines)
                {
                    using var ins = new SqliteCommand(@"
                        INSERT INTO Transactions
                            (Account, Date, CheckNo, Amount, Name, Memo,
                             Category, TrnType, Clear)
                        VALUES
                            (@acct,@date,@chk,@amt,@name,@memo,
                             @cat,@trn,@clr);", conn);

                    ins.Parameters.AddWithValue("@acct", _account);
                    ins.Parameters.AddWithValue("@date", _row.DateDisplay);
                    ins.Parameters.AddWithValue("@chk",  _row.CheckNo ?? "");
                    ins.Parameters.AddWithValue("@amt",  (double)line.AmountDecimal);
                    ins.Parameters.AddWithValue("@name", _row.Name ?? "");
                    ins.Parameters.AddWithValue("@memo", line.Memo ?? "");
                    ins.Parameters.AddWithValue("@cat",  line.Category ?? "");
                    ins.Parameters.AddWithValue("@trn",  _row.TrnType ?? "");
                    ins.Parameters.AddWithValue("@clr",  _row.Clear ? 1 : 0);
                    ins.ExecuteNonQuery();
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save split:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
