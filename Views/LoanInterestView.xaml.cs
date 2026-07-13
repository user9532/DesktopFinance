using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public class LoanInterestRow
    {
        public string  Date     { get; set; } = "";
        public string  Name     { get; set; } = "";
        public decimal Amount   { get; set; }
        public decimal Balance  { get; set; }
        public string  Category { get; set; } = "";
        public string  Memo     { get; set; } = "";
    }

    public partial class LoanInterestView : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly string _account;

        public string Account => _account;

        public LoanInterestView(DatabaseManager db, string account)
        {
            InitializeComponent();
            _db      = db;
            _account = account;
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var rows = new List<LoanInterestRow>();

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Date, Name, Amount, Category, Memo " +
                "FROM Transactions " +
                "WHERE Source = @acct " +
                "ORDER BY Date ASC;", conn);
            cmd.Parameters.AddWithValue("@acct", _account);
            using var rdr = cmd.ExecuteReader();

            decimal running = 0m;
            while (rdr.Read())
            {
                var amount = rdr.IsDBNull(2) ? 0m : Convert.ToDecimal(rdr.GetDouble(2));
                running += amount;

                rows.Add(new LoanInterestRow
                {
                    Date     = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                    Name     = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                    Amount   = amount,
                    Balance  = running,
                    Category = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                    Memo     = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                });
            }

            InterestGrid.ItemsSource = rows;
            TxtTotalBalance.Text = running.ToString("N2");
        }
    }
}
