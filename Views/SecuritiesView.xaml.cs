using FinanceApp.Data;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Windows.Controls;

namespace FinanceApp.Views
{
    public partial class SecuritiesView : UserControl
    {
        private readonly DatabaseManager _db;

        public SecuritiesView(DatabaseManager db)
        {
            _db = db;
            InitializeComponent();
            LoadData();
        }

        public void Refresh() => LoadData();

        private void LoadData()
        {
            var rows = new List<SecurityRowViewModel>();

            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT Symbol, Description FROM Securities ORDER BY Symbol;", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new SecurityRowViewModel
                {
                    Symbol      = r.IsDBNull(0) ? "" : r.GetString(0),
                    Description = r.IsDBNull(1) ? "" : r.GetString(1)
                });
            }

            SecGrid.ItemsSource = rows;
        }
    }

    public class SecurityRowViewModel
    {
        public string Symbol      { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
