using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class AccountsAndCategoriesView : UserControl
    {
        private readonly DatabaseManager _db;

        public AccountsAndCategoriesView(DatabaseManager db)
        {
            _db = db;
            InitializeComponent();
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var rows = new List<AccountChartRowViewModel>();

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountName, AccountType, AccountGroup FROM AccountChart " +
                "ORDER BY CASE AccountGroup " +
                "    WHEN 'Asset'     THEN 1 " +
                "    WHEN 'Liability' THEN 2 " +
                "    WHEN 'Income'    THEN 3 " +
                "    WHEN 'Expense'   THEN 4 " +
                "    ELSE 5 END, AccountName;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new AccountChartRowViewModel
                {
                    AccountName  = r.IsDBNull(0) ? "" : r.GetString(0),
                    AccountType  = r.IsDBNull(1) ? "" : r.GetString(1),
                    AccountGroup = r.IsDBNull(2) ? "" : r.GetString(2)
                });
            }

            AcctGrid.ItemsSource = rows;
        }
    }

    public class AccountChartRowViewModel
    {
        public string AccountName  { get; set; } = "";
        public string AccountType  { get; set; } = "";
        public string AccountGroup { get; set; } = "";
    }
}
