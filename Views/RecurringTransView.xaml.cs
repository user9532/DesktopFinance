using FinanceApp.Data;
using FinanceApp.Dialogs;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public class RecurringTransRow
    {
        public long    Id             { get; set; }
        public string  Payee          { get; set; } = "";
        public string  Account        { get; set; } = "";
        public string  Category       { get; set; } = "";
        public decimal Amount         { get; set; }
        public bool    IsSplit        { get; set; }
        public int     TrType         { get; set; }
        public int     Frequency      { get; set; }
        public string  NextDue        { get; set; } = "";
        public string  LastPosted     { get; set; } = "";

        public string AmountDisplay => IsSplit ? "SPLIT" : Amount.ToString("N2");

        // Loan payments (TrType 3) are edited via the loan flow, not this dialog.
        public bool IsEditable => TrType != 3;

        public string FrequencyLabel => Frequency switch
        {
            1   => "Daily",
            7   => "Weekly",
            14  => "Bi-Weekly",
            30  => "Monthly",
            90  => "Quarterly",
            180 => "Semi-Annual",
            365 => "Annual",
            _   => Frequency.ToString()
        };
    }

    public partial class RecurringTransView : UserControl
    {
        private readonly DatabaseManager _db;

        public RecurringTransView(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var payments = new List<RecurringTransRow>();
            var receipts = new List<RecurringTransRow>();

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Id, Name, Account, Category, Amount, Frequency, NextDue, LastPosted, TrType " +
                "FROM RecurTrans " +
                "WHERE TrType IN (1, 2, 3) " +
                "ORDER BY CASE TrType WHEN 2 THEN 1 ELSE 0 END, " +
                "         CASE TrType WHEN 3 THEN 1 ELSE 0 END, " +
                "         NextDue, Id, Name;", conn);
            using var rdr = cmd.ExecuteReader();

            var allRows = new List<RecurringTransRow>();
            while (rdr.Read())
            {
                allRows.Add(new RecurringTransRow
                {
                    Id         = rdr.IsDBNull(0) ? 0  : rdr.GetInt64(0),
                    Payee      = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    Account    = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                    Category   = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                    Amount     = rdr.IsDBNull(4) ? 0m : Convert.ToDecimal(rdr.GetDouble(4)),
                    Frequency  = rdr.IsDBNull(5) ? 0  : rdr.GetInt32(5),
                    NextDue    = rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                    LastPosted = rdr.IsDBNull(7) ? "" : rdr.GetString(7),
                    TrType     = rdr.IsDBNull(8) ? 1  : rdr.GetInt32(8),
                });
            }

            // Group by Id; first row per Id is the display row; mark splits
            var seen   = new HashSet<long>();
            var counts = new Dictionary<long, int>();
            foreach (var r in allRows)
                counts[r.Id] = counts.TryGetValue(r.Id, out var c) ? c + 1 : 1;

            foreach (var r in allRows)
            {
                if (!seen.Add(r.Id)) continue;
                r.IsSplit = counts[r.Id] > 1;
                if (r.TrType == 2)
                    receipts.Add(r);
                else
                    payments.Add(r);
            }

            PaymentsGrid.ItemsSource = payments;
            ReceiptsGrid.ItemsSource = receipts;
        }

        private void NewPayment_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new RecurringPaymentDialog(_db) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadData();
                (Window.GetWindow(this) as MainWindow)?.RefreshHome();
            }
        }

        private void NewReceipt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new RecurringReceiptDialog(_db) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadData();
                (Window.GetWindow(this) as MainWindow)?.RefreshHome();
            }
        }

        private void ViewPaymentRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RecurringTransRow row)
            {
                if (row.TrType == 3)
                {
                    var loanDlg = new LoanPaymentDialog(_db, row.Account)
                        { Owner = Window.GetWindow(this) };
                    if (loanDlg.ShowDialog() == true)
                    {
                        LoadData();
                        if (Window.GetWindow(this) is MainWindow main)
                            main.RefreshLoanTabs(row.Account);
                    }
                    return;
                }

                var detail = new RecurringDetailDialog(_db, row.Id, trType: 1)
                    { Owner = Window.GetWindow(this) };
                if (detail.ShowDialog() == true)
                {
                    LoadData();
                    if (Window.GetWindow(this) is MainWindow main)
                    {
                        main.RefreshHome();
                        main.RefreshBankStatementTab(row.Account);
                    }
                }
            }
        }

        private void ViewReceiptRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RecurringTransRow row)
            {
                var detail = new RecurringDetailDialog(_db, row.Id, trType: 2)
                    { Owner = Window.GetWindow(this) };
                if (detail.ShowDialog() == true)
                {
                    LoadData();
                    if (Window.GetWindow(this) is MainWindow main)
                    {
                        main.RefreshHome();
                        main.RefreshBankStatementTab(row.Account);
                    }
                }
            }
        }

        private void EditPaymentRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RecurringTransRow row) return;
            if (row.TrType == 3) return; // Loan payments are edited via the loan flow, not here.

            var dlg = new RecurringPaymentDialog(_db, row.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadData();
                (Window.GetWindow(this) as MainWindow)?.RefreshHome();
            }
        }

        private void EditReceiptRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not RecurringTransRow row) return;

            var dlg = new RecurringReceiptDialog(_db, row.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadData();
                (Window.GetWindow(this) as MainWindow)?.RefreshHome();
            }
        }
    }
}
