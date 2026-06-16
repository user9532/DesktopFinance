using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class InvestmentStatementView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public InvestmentStatementView(DatabaseManager db, string account)
        {
            _db      = db;
            _account = account;
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            var rows = new ObservableCollection<InvestmentRowViewModel>();

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(@"
                SELECT Date, CheckNo, Name, Category, Memo, Action, Symbol,
                       Quantity, Price, Commission, Amount, TrnType, Account
                FROM   Transactions
                WHERE  Account = @acct OR Category = @acct
                ORDER  BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var r = cmd.ExecuteReader();

            decimal running = 0;
            while (r.Read())
            {
                var trnType = r.IsDBNull(11) ? "" : r.GetString(11);
                var rawAmt  = r.IsDBNull(10) ? 0m : (decimal)r.GetDouble(10);
                var acctCol = r.IsDBNull(12) ? "" : r.GetString(12);
                var viacat   = acctCol != _account;   // matched via Category column

                if (viacat)
                    trnType = trnType == "DEBIT" ? "CREDIT" : "DEBIT";

                // CREDIT → negative amount; DEBIT → positive amount
                var signedAmt = trnType == "CREDIT" ? -rawAmt : rawAmt;
                running += signedAmt;

                var row = new InvestmentRowViewModel
                {
                    Date          = r.IsDBNull(0) ? "" : r.GetString(0),
                    CheckNo       = r.IsDBNull(1) ? "" : r.GetString(1),
                    Name          = r.IsDBNull(2) ? "" : r.GetString(2),
                    Category      = r.IsDBNull(3) ? "" : r.GetString(3),
                    Memo          = r.IsDBNull(4) ? "" : r.GetString(4),
                    Action        = r.IsDBNull(5) ? "" : r.GetString(5),
                    Symbol        = r.IsDBNull(6) ? "" : r.GetString(6),
                    Quantity      = r.IsDBNull(7) ? 0  : (decimal)r.GetDouble(7),
                    Price         = r.IsDBNull(8) ? 0  : (decimal)r.GetDouble(8),
                    Commission    = r.IsDBNull(9) ? 0  : (decimal)r.GetDouble(9),
                    SignedAmount  = signedAmt,
                    Balance       = running
                };
                rows.Add(row);
            }

            TrGrid.ItemsSource = rows;
        }

        public void Refresh() => LoadData();

        private void Transaction_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.InvestmentTransactionDialog(_db, _account)
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
            LoadData();
        }

        private void Trade_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Dialogs.InvestmentTradeDialog(_db, _account)
            {
                Owner = Window.GetWindow(this)
            };
            dlg.ShowDialog();
            LoadData();
        }
    }

    public class InvestmentRowViewModel
    {
        public string  Date         { get; set; } = "";
        public string  CheckNo      { get; set; } = "";
        public string  Name         { get; set; } = "";
        public string  Category     { get; set; } = "";
        public string  Memo         { get; set; } = "";
        public string  Action       { get; set; } = "";
        public string  Symbol       { get; set; } = "";
        public decimal Quantity     { get; set; }
        public decimal Price        { get; set; }
        public decimal Commission   { get; set; }
        public decimal SignedAmount { get; set; }
        public decimal Balance      { get; set; }

        public string AmountDisplay => SignedAmount.ToString("N2");
    }
}
