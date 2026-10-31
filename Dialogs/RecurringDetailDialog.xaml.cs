using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Dialogs
{
    // One editable line in the dialog (Account moved to shared header)
    internal class RecurLineControls
    {
        public TextBox TxtPayee    { get; } = new TextBox { MinWidth = 180 };
        public TextBox TxtAmount   { get; } = new TextBox { MinWidth = 100 };
        public TextBox TxtCategory { get; } = new TextBox { MinWidth = 180 };
        public TextBox TxtMemo     { get; } = new TextBox { MinWidth = 120 };
    }

    public partial class RecurringDetailDialog : Window
    {
        private readonly DatabaseManager         _db;
        private readonly long                    _id;
        private readonly int                     _trType;   // 1 = payment, 2 = receipt
        private readonly List<RecurLineControls> _lines = new();
        private          int                     _frequency;

        // The account the transaction was actually posted to (set on Post),
        // which may differ from the account the item was originally associated
        // with if the user changed the account dropdown before posting.
        public string PostedAccount { get; private set; } = "";

        public RecurringDetailDialog(DatabaseManager db, long id, int trType = 1)
        {
            _db     = db;
            _id     = id;
            _trType = trType;
            InitializeComponent();
            LoadBankAccounts();
            LoadRows();
        }

        private void LoadBankAccounts()
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart " +
                "WHERE AccountType = 'Bank' ORDER BY AccountName;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
                CbAccount.Items.Add(rdr.GetString(0));
        }

        private void LoadRows()
        {
            if (_trType == 2)
                PayeeHeader.Text = "Payor";

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Name, Account, Amount, Category, Memo, Frequency, NextDue " +
                "FROM RecurTrans WHERE Id = @id ORDER BY rowid;", conn);
            cmd.Parameters.AddWithValue("@id", _id);
            using var rdr = cmd.ExecuteReader();

            bool first = true;
            while (rdr.Read())
            {
                var payee    = rdr.IsDBNull(0) ? "" : rdr.GetString(0);
                var account  = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                var amount   = rdr.IsDBNull(2) ? 0.0 : rdr.GetDouble(2);
                var category = rdr.IsDBNull(3) ? "" : rdr.GetString(3);
                var memo     = rdr.IsDBNull(4) ? "" : rdr.GetString(4);
                var freq     = rdr.IsDBNull(5) ? 0 : rdr.GetInt32(5);
                var nextDue  = rdr.IsDBNull(6) ? "" : rdr.GetString(6);

                if (first)
                {
                    _frequency = freq;
                    TxtFrequency.Text = FrequencyLabel(freq);
                    if (DateTime.TryParse(nextDue, out var nd))
                        DpNextDue.SelectedDate = nd;

                    // Select the account in the dropdown; fall back to text if not in list
                    CbAccount.SelectedItem = account;
                    if (CbAccount.SelectedItem == null)
                        CbAccount.Text = account;

                    first = false;
                }

                AddLine(payee, amount.ToString("N2"), category, memo);
            }
        }

        private void AddLine(string payee, string amount, string category, string memo)
        {
            var line = new RecurLineControls();
            line.TxtPayee.Text    = payee;
            line.TxtAmount.Text   = amount;
            line.TxtCategory.Text = category;
            line.TxtMemo.Text     = memo;

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(line.TxtPayee,    0);
            Grid.SetColumn(line.TxtAmount,   2);
            Grid.SetColumn(line.TxtCategory, 4);
            Grid.SetColumn(line.TxtMemo,     6);

            row.Children.Add(line.TxtPayee);
            row.Children.Add(line.TxtAmount);
            row.Children.Add(line.TxtCategory);
            row.Children.Add(line.TxtMemo);

            LinesPanel.Children.Add(row);
            _lines.Add(line);
        }

        private void Post_Click(object sender, RoutedEventArgs e)
        {
            if (DpNextDue.SelectedDate is null)
            {
                MessageBox.Show("Next Due Date is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var account = (CbAccount.SelectedItem as string) ?? CbAccount.Text.Trim();
            if (string.IsNullOrEmpty(account))
            {
                MessageBox.Show("Please select a bank account.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var nextDueDate = DpNextDue.SelectedDate.Value;
            var nextDueStr  = nextDueDate.ToString("yyyy-MM-dd");
            var newNextDue  = AdvanceDate(nextDueDate, _frequency).ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();
            using var tx   = conn.BeginTransaction();

            foreach (var line in _lines)
            {
                if (!decimal.TryParse(line.TxtAmount.Text, out var amt))
                    amt = 0m;

                string typeVal, trnType;
                if (_trType == 2)
                {
                    // Receipt: positive → DEBIT, negative → CREDIT
                    typeVal = "CREDIT";
                    trnType = amt < 0 ? "CREDIT" : "DEBIT";
                    amt     = Math.Abs(amt);
                }
                else
                {
                    // Payment: positive → CREDIT, negative → DEBIT
                    typeVal = "DEBIT";
                    trnType = amt < 0 ? "DEBIT" : "CREDIT";
                    amt     = Math.Abs(amt);
                }

                using var ins = new SqliteCommand(@"
                    INSERT INTO Transactions
                        (Account, Date, Name, Category, Amount, Memo, Type, TrnType)
                    VALUES
                        (@acct, @date, @name, @cat, @amt, @memo, @type, @trnType);",
                    conn, tx);
                ins.Parameters.AddWithValue("@acct",    account);
                ins.Parameters.AddWithValue("@date",    nextDueStr);
                ins.Parameters.AddWithValue("@name",    line.TxtPayee.Text.Trim());
                ins.Parameters.AddWithValue("@cat",     line.TxtCategory.Text.Trim());
                ins.Parameters.AddWithValue("@amt",     (double)amt);
                ins.Parameters.AddWithValue("@memo",    line.TxtMemo.Text.Trim());
                ins.Parameters.AddWithValue("@type",    typeVal);
                ins.Parameters.AddWithValue("@trnType", trnType);
                ins.ExecuteNonQuery();
            }

            using var upd = new SqliteCommand(@"
                UPDATE RecurTrans
                SET    LastPosted = @last, NextDue = @next
                WHERE  Id = @id;", conn, tx);
            upd.Parameters.AddWithValue("@last", nextDueStr);
            upd.Parameters.AddWithValue("@next", newNextDue);
            upd.Parameters.AddWithValue("@id",   _id);
            upd.ExecuteNonQuery();

            tx.Commit();
            PostedAccount = account;
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            if (DpNextDue.SelectedDate is null)
            {
                MessageBox.Show("Next Due Date is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var nextDueDate = DpNextDue.SelectedDate.Value;
            var newNextDue  = AdvanceDate(nextDueDate, _frequency).ToString("yyyy-MM-dd");

            using var conn = _db.OpenConnection();
            using var upd = new SqliteCommand(@"
                UPDATE RecurTrans
                SET    NextDue = @next
                WHERE  Id = @id;", conn);
            upd.Parameters.AddWithValue("@next", newNextDue);
            upd.Parameters.AddWithValue("@id",   _id);
            upd.ExecuteNonQuery();

            DialogResult = true;
        }

        private static DateTime AdvanceDate(DateTime date, int frequency) => frequency switch
        {
            1   => date.AddDays(1),
            7   => date.AddDays(7),
            14  => date.AddDays(14),
            30  => date.AddMonths(1),
            90  => date.AddMonths(3),
            180 => date.AddMonths(6),
            365 => date.AddYears(1),
            _   => date.AddDays(frequency)
        };

        private static string FrequencyLabel(int freq) => freq switch
        {
            1   => "Daily",
            7   => "Weekly",
            14  => "Bi-Weekly",
            30  => "Monthly",
            90  => "Quarterly",
            180 => "Semi-Annual",
            365 => "Annual",
            _   => freq.ToString()
        };
    }
}
