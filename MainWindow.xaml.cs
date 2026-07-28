using System.IO;
using FinanceApp.Data;
using FinanceApp.Dialogs;
using FinanceApp.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FinanceApp
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseManager _db;
        private bool _leftPaneVisible = true;
        private double _leftPaneWidth = 200;

        public MainWindow()
        {
            InitializeComponent();
            _db = new DatabaseManager();
            _db.InitializeDatabase();
            LoadNavTree();

            _homeView = new HomeView(_db);
            HomeTabContent.Children.Add(_homeView);
        }

        private readonly HomeView _homeView;

        // ── navigation tree ─────────────────────────────────────────────────

        // AccountType values, each under its own top-level branch, in display order.
        private static readonly (string Branch, string AccountType)[] NavBranches =
        {
            ("Bank",            "Bank"),
            ("Credit Card",     "Credit Card"),
            ("Investment",      "Investment"),
            ("Other Asset",     "Other Asset"),
            ("Loan",            "Loan"),
            ("Other Liability", "Other Liability"),
        };

        public void LoadNavTree()
        {
            NavTree.Items.Clear();

            using var conn = _db.OpenConnection();

            foreach (var (branch, accountType) in NavBranches)
            {
                var branchNode = new TreeViewItem
                {
                    Header     = branch.ToUpperInvariant(),
                    FontWeight = FontWeights.Bold,
                    IsExpanded = true
                };

                using var cmd = new SqliteCommand(
                    "SELECT AccountName FROM AccountChart WHERE AccountType = @t ORDER BY AccountName;", conn);
                cmd.Parameters.AddWithValue("@t", accountType);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var accountName = r.GetString(0);
                    var leafNode = new TreeViewItem
                    {
                        Header     = accountName,
                        FontWeight = FontWeights.Normal,
                        Tag        = (accountType, accountName)
                    };
                    // PreviewMouseLeftButtonUp fires on every click, even when the item
                    // was already selected (SelectedItemChanged only fires on an actual
                    // selection change, so re-clicking the same leaf after focus moved
                    // to another tab would otherwise do nothing).
                    leafNode.PreviewMouseLeftButtonUp += NavTreeLeaf_Click;
                    branchNode.Items.Add(leafNode);
                }

                NavTree.Items.Add(branchNode);
            }
        }

        public void RefreshNavTree() => LoadNavTree();

        private void NavTreeLeaf_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem { Tag: ValueTuple<string, string> tag }) return;

            // Only handle clicks on this item itself, not bubbled clicks from a
            // nested item (not applicable here since leaves have no children, but
            // keeps the handler correct if that ever changes).
            if (!ReferenceEquals(e.OriginalSource is DependencyObject d
                    ? FindAncestorTreeViewItem(d) : null, sender)) return;

            e.Handled = true;
            NavigateToAccount(tag.Item1, tag.Item2);
        }

        private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TreeViewItem tvi) return tvi;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private void NavigateToAccount(string accountType, string accountName)
        {
            switch (accountType)
            {
                case "Bank":
                    ActivateOrOpenStatement(accountName);
                    break;
                case "Credit Card":
                    ActivateOrOpenCreditCardStatement(accountName);
                    break;
                case "Investment":
                    ActivateOrOpenInvestmentStatementByAccount(accountName);
                    break;
                case "Other Asset":
                    ActivateOrOpenOtherAccountStatement(accountName);
                    break;
                case "Loan":
                    ActivateOrOpenLoanStatement(accountName);
                    break;
                case "Other Liability":
                    ActivateOrOpenOtherLiabilityStatement(accountName);
                    break;
            }
        }

        private void ActivateOrOpenInvestmentStatementByAccount(string account)
        {
            var header = $"Investment: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == header)
                {
                    (item.Content as InvestmentStatementView)?.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new InvestmentStatementView(_db, account);
            AddClosableTab(header, view);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new OpenFileDialog
            {
                Title  = "Select Backup File to Restore",
                Filter = "Finance Database (*.fdb)|*.fdb|All Files (*.*)|*.*"
            };
            if (openDlg.ShowDialog() != true) return;

            var confirm = MessageBox.Show(
                "Restoring a backup will replace the current database and close all open tabs.\n\n" +
                "Any unsaved changes will be lost. Continue?",
                "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // Close all statement tabs without prompting for saves.
                var tabs = MainTabControl.Items.OfType<TabItem>()
                    .Where(t => t.Content is AccountStatementView)
                    .ToList();
                foreach (var tab in tabs)
                    MainTabControl.Items.Remove(tab);

                // Release SQLite connections held by the connection pool.
                SqliteConnection.ClearAllPools();

                File.Copy(openDlg.FileName, _db.DbPath, overwrite: true);
                _db.InitializeDatabase();

                MessageBox.Show("Backup restored successfully.",
                    "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed:\n\n{ex.Message}",
                    "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title            = "Save Database Backup",
                Filter           = "Finance Database (*.fdb)|*.fdb|All Files (*.*)|*.*",
                FileName         = $"MyFinance_backup_{DateTime.Now:yyyyMMdd_HHmmss}.fdb",
                DefaultExt       = ".fdb"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                File.Copy(_db.DbPath, saveDlg.FileName, overwrite: true);
                MessageBox.Show($"Backup created successfully:\n{saveDlg.FileName}",
                    "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Backup failed:\n\n{ex.Message}",
                    "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            var dirtyBank = MainTabControl.Items
                .OfType<TabItem>()
                .Select(t => t.Content as AccountStatementView)
                .OfType<AccountStatementView>()          // strips nulls, non-nullable
                .Where(v => v.HasUnsavedChanges)
                .ToList();

            var dirtyCc = MainTabControl.Items
                .OfType<TabItem>()
                .Select(t => t.Content as CreditCardStatementView)
                .OfType<CreditCardStatementView>()
                .Where(v => v.HasUnsavedChanges)
                .ToList();

            var dirtyOther = MainTabControl.Items
                .OfType<TabItem>()
                .Select(t => t.Content as OtherAccountStatementView)
                .OfType<OtherAccountStatementView>()
                .Where(v => v.HasUnsavedChanges)
                .ToList();

            var dirtyOtherLiability = MainTabControl.Items
                .OfType<TabItem>()
                .Select(t => t.Content as OtherLiabilityStatementView)
                .OfType<OtherLiabilityStatementView>()
                .Where(v => v.HasUnsavedChanges)
                .ToList();

            if (!dirtyBank.Any() && !dirtyCc.Any() && !dirtyOther.Any() && !dirtyOtherLiability.Any()) return;

            var result = MessageBox.Show(
                "There are unsaved changes in one or more account statement tabs.\n\n" +
                "Do you want to save before exiting?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var v in dirtyBank)            v.SaveChanges();
                foreach (var v in dirtyCc)              v.SaveChanges();
                foreach (var v in dirtyOther)           v.SaveChanges();
                foreach (var v in dirtyOtherLiability)  v.SaveChanges();
            }
            else if (result == MessageBoxResult.Cancel)
                e.Cancel = true;
        }

        private void ImportQfx_Click(object sender, RoutedEventArgs e)
        {
            var openDlg = new OpenFileDialog
            {
                Title  = "Select QFX File",
                Filter = "QFX Files (*.qfx)|*.qfx|All Files (*.*)|*.*"
            };
            if (openDlg.ShowDialog() != true) return;

            var filePath = openDlg.FileName;
            var importer = new QfxImporter(_db);

            try
            {
                // Resolve ORG:FID → account name
                var (org, fid) = importer.ReadOrgFid(filePath);
                var orgFid     = $"{org}:{fid}";

                var accountName = importer.LookupAccountMap(orgFid);

                if (accountName == null)
                {
                    var mapDlg = new AccountMapDialog(_db, org, fid) { Owner = this };
                    if (mapDlg.ShowDialog() != true) return;

                    accountName = mapDlg.SelectedAccount;
                    importer.SaveAccountMap(accountName, orgFid);
                }

                // Parse and import
                var transactions = importer.ReadTransactions(filePath);
                importer.ImportTransactions(accountName, transactions);

                // Activate existing statement tab (refreshed) or open a new one,
                // choosing the Bank or Credit Card statement window based on AccountType
                var accountType = GetAccountType(accountName);
                if (accountType == "Credit Card")
                    ActivateOrOpenCreditCardStatement(accountName);
                else
                    ActivateOrOpenStatement(accountName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n\n{ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportTransactions_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title      = "Export Transactions",
                Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName   = $"Transactions_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                var columns = new[]
                {
                    "Account", "Date", "CheckNo", "Type", "Amount", "Name", "Memo",
                    "Category", "TrnType", "Source", "Clear", "FinancialId", "Action",
                    "Symbol", "Quantity", "Price", "Commission"
                };

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", columns));

                using var conn = _db.OpenConnection();
                using var cmd = new SqliteCommand(
                    $"SELECT {string.Join(", ", columns)} FROM Transactions;", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var fields = new string[columns.Length];
                    for (int i = 0; i < columns.Length; i++)
                        fields[i] = CsvEscape(r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "");
                    sb.AppendLine(string.Join(",", fields));
                }

                File.WriteAllText(saveDlg.FileName, sb.ToString(), Encoding.UTF8);

                MessageBox.Show($"Transactions exported successfully:\n{saveDlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n\n{ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string CsvEscape(string s) =>
            s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        private string GetAccountType(string accountName)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT AccountType FROM AccountChart WHERE AccountName = @acct;", conn);
            cmd.Parameters.AddWithValue("@acct", accountName);
            return cmd.ExecuteScalar() as string ?? "";
        }

        private void AddSecurities_Click(object sender, RoutedEventArgs e)
        {
            new AddSecurityDialog(_db) { Owner = this }.ShowDialog();
        }

        private void AddQuotes_Click(object sender, RoutedEventArgs e)
        {
            new AddQuoteDialog(_db) { Owner = this }.ShowDialog();
        }

        private void InvestmentStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InvestmentAccountSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var account = dlg.SelectedAccount!;
            var header  = $"Investment: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == header)
                {
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new InvestmentStatementView(_db, account);
            AddClosableTab(header, view);
        }

        private void StockSplit_Click(object sender, RoutedEventArgs e)
        {
            new StockSplitDialog(_db) { Owner = this }.ShowDialog();
        }

        private void PortfolioStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PortfolioSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            ActivateOrOpenPortfolio(dlg.SelectedPortfolio!);
        }

        public void ActivateOrOpenInvestmentStatement(string portfolio)
        {
            string account = "";
            using (var conn = _db.OpenConnection())
            using (var cmd = new SqliteCommand(
                "SELECT AccountName FROM AccountChart WHERE Portfolio = @p AND AccountCode = 2 LIMIT 1;", conn))
            {
                cmd.Parameters.AddWithValue("@p", portfolio);
                account = cmd.ExecuteScalar() as string ?? "";
            }
            if (string.IsNullOrEmpty(account)) return;

            var header = $"Investment: {account}";
            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == header)
                {
                    (item.Content as InvestmentStatementView)?.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }
            var view = new InvestmentStatementView(_db, account);
            AddClosableTab(header, view);
        }

        public void ActivateOrOpenPortfolio(string portfolio)
        {
            var header = $"Portfolio: {portfolio}";
            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == header)
                {
                    (item.Content as PortfolioView)?.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }
            var view = new PortfolioView(_db, portfolio);
            AddClosableTab(header, view);
        }

        private void IncomeExpenses_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new IncomeExpensesDateDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var view   = new IncomeExpensesView(_db, dlg.StartDate, dlg.EndDate);
            var header = $"Income & Expenses  {dlg.StartDate:MM/dd/yyyy}–{dlg.EndDate:MM/dd/yyyy}";
            AddClosableTab(header, view);
        }

        private void DrilldownCategory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new DrilldownCategoryDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var view   = new DrilldownCategoryView(_db, dlg.SelectedCategory);
            var header = $"Drilldown: {dlg.SelectedCategory}";
            AddClosableTab(header, view);
        }

        private void BudgetActual_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new IncomeExpensesDateDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var view   = new BudgetActualView(_db, dlg.StartDate, dlg.EndDate);
            var header = $"Budget vs Actual  {dlg.StartDate:MM/dd/yyyy}–{dlg.EndDate:MM/dd/yyyy}";
            AddClosableTab(header, view);
        }

        private void NewAccount_Click(object sender, RoutedEventArgs e)
        {
            var typeDialog = new AccountTypeDialog { Owner = this };
            if (typeDialog.ShowDialog() != true) return;

            bool added = false;
            switch (typeDialog.SelectedType)
            {
                case NewAccountType.Bank:
                case NewAccountType.CreditCard:
                case NewAccountType.Investment:
                    added = new StandardAccountDialog(_db, typeDialog.SelectedType) { Owner = this }.ShowDialog() == true;
                    break;

                case NewAccountType.Loan:
                    added = new LoanAccountDialog(_db) { Owner = this }.ShowDialog() == true;
                    break;

                case NewAccountType.OtherAsset:
                case NewAccountType.OtherLiability:
                    added = new OtherAccountDialog(_db, typeDialog.SelectedType) { Owner = this }.ShowDialog() == true;
                    break;
            }

            if (added)
            {
                RefreshAllStatementCategories();
                RefreshNavTree();
            }
        }

        private void EditAccount_Click(object sender, RoutedEventArgs e)
        {
            var selectDlg = new EditAccountSelectDialog(_db) { Owner = this };
            if (selectDlg.ShowDialog() != true) return;

            var account = selectDlg.SelectedAccount;

            if (AccountInTransactions(account))
            {
                MessageBox.Show(
                    "The selected account is already assigned to transactions. Name cannot be changed.",
                    "Cannot Edit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (new EditAccountDialog(_db, account) { Owner = this }.ShowDialog() == true)
            {
                RefreshAllStatementCategories();
                RefreshNavTree();
            }
        }

        private bool AccountInTransactions(string accountName)
        {
            using var conn = _db.OpenConnection();
            using var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM Transactions " +
                "WHERE Account = @name OR Category = @name;", conn);
            cmd.Parameters.AddWithValue("@name", accountName);
            return (long)cmd.ExecuteScalar()! > 0;
        }

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            if (new NewCategoryDialog(_db) { Owner = this }.ShowDialog() == true)
                RefreshAllStatementCategories();
        }

        private void EditCategory_Click(object sender, RoutedEventArgs e)
        {
            var selectDlg = new EditCategorySelectDialog(_db) { Owner = this };
            if (selectDlg.ShowDialog() != true) return;

            var category = selectDlg.SelectedCategory;

            if (AccountInTransactions(category))
            {
                MessageBox.Show(
                    "The selected category is already assigned to transactions. Name cannot be changed.",
                    "Cannot Edit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (new EditCategoryDialog(_db, category) { Owner = this }.ShowDialog() == true)
                RefreshAllStatementCategories();
        }

        private void EnterBudget_Click(object sender, RoutedEventArgs e)
        {
            new EnterBudgetDialog(_db) { Owner = this }.ShowDialog();
        }

        private void RefreshAllStatementCategories()
        {
            foreach (var tab in MainTabControl.Items.OfType<TabItem>())
                if (tab.Content is AccountStatementView v)
                    v.RefreshCategories();
        }

        // ── statement tab helper ──────────────────────────────────────────

        // Activates and refreshes an existing Statement tab for the given account,
        // or creates a new one if none exists.
        private void ActivateOrOpenStatement(string account)
        {
            var tabHeader = $"Statement: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is AccountStatementView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    if (!existing.HasUnsavedChanges)
                        existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            // No existing tab — create a new one
            var view = new AccountStatementView(_db, account);
            AddClosableTab(tabHeader, view, () => view.CanClose());
        }

        // ── status bar ────────────────────────────────────────────────────

        public void SetStatus(string message) => StatusText.Text = message;

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignore bubbled SelectionChanged events from controls inside the tab content.
            if (!e.OriginalSource.Equals(MainTabControl)) return;

            if (MainTabControl.SelectedItem is not TabItem tab) return;
            var header = GetTabHeaderText(tab);
            StatusText.Text = string.IsNullOrEmpty(header) || header == "Home"
                ? "Ready"
                : $"Viewing: {header}";

            if (tab.Content is AccountStatementView view && !view.HasUnsavedChanges)
                view.Refresh();
            else if (tab.Content is InvestmentStatementView invView)
                invView.Refresh();
            else if (tab.Content is PortfolioView pfView)
                pfView.Refresh();
            else if (ReferenceEquals(tab, HomeTab))
                _homeView.Refresh();
        }

        private static string GetTabHeaderText(TabItem tab)
        {
            if (tab.Header is string s) return s;
            if (tab.Header is StackPanel sp)
                foreach (UIElement child in sp.Children)
                    if (child is TextBlock tb) return tb.Text;
            return "";
        }

        private void ToggleLeftPane_Click(object sender, RoutedEventArgs e)
        {
            if (_leftPaneVisible)
            {
                _leftPaneWidth = LeftColumn.Width.Value;
                LeftColumn.Width = new GridLength(0);
                LeftColumn.MinWidth = 0;
                ToggleBtn.Content = "►"; // ►
            }
            else
            {
                LeftColumn.MinWidth = 30;
                LeftColumn.Width = new GridLength(_leftPaneWidth);
                ToggleBtn.Content = "◄"; // ◄
            }

            _leftPaneVisible = !_leftPaneVisible;
        }

        private void BankStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new BankSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenStatement(dlg.SelectedAccount);
        }

        private void CreditCardStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CreditCardSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenCreditCardStatement(dlg.SelectedAccount);
        }

        private void ActivateOrOpenCreditCardStatement(string account)
        {
            var tabHeader = $"Statement: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is CreditCardStatementView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    if (!existing.HasUnsavedChanges)
                        existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new CreditCardStatementView(_db, account);
            AddClosableTab(tabHeader, view, () => view.CanClose());
        }

        private void OtherAccountStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OtherAccountSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenOtherAccountStatement(dlg.SelectedAccount);
        }

        private void ActivateOrOpenOtherAccountStatement(string account)
        {
            var tabHeader = $"Statement: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is OtherAccountStatementView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    if (!existing.HasUnsavedChanges)
                        existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new OtherAccountStatementView(_db, account);
            AddClosableTab(tabHeader, view, () => view.CanClose());
        }

        private void OtherLiabilityStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OtherLiabilitySelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenOtherLiabilityStatement(dlg.SelectedAccount);
        }

        private void ActivateOrOpenOtherLiabilityStatement(string account)
        {
            var tabHeader = $"Statement: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is OtherLiabilityStatementView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    if (!existing.HasUnsavedChanges)
                        existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new OtherLiabilityStatementView(_db, account);
            AddClosableTab(tabHeader, view, () => view.CanClose());
        }

        private void NetWorthStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NetWorthDateDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var tabHeader = $"Net Worth: {dlg.SelectedDate:yyyy-MM-dd}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is NetWorthView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new NetWorthView(_db, dlg.SelectedDate);
            AddClosableTab(tabHeader, view);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog { Owner = this }.ShowDialog();
        }

        private void CashFlowStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CashFlowDateDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var tabHeader = $"Cash Flow: {dlg.StartDate:yyyy-MM-dd} - {dlg.EndDate:yyyy-MM-dd}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is CashFlowView existing
                    && GetTabHeaderText(item) == tabHeader)
                {
                    existing.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new CashFlowView(_db, dlg.StartDate, dlg.EndDate);
            AddClosableTab(tabHeader, view);
        }

        private void RecurringPayment_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new RecurringPaymentDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            ActivateOrOpenRecurring();
            _homeView.Refresh();
        }

        private void RecurringReceipt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new RecurringReceiptDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            ActivateOrOpenRecurring();
            _homeView.Refresh();
        }

        private void ViewRecurring_Click(object sender, RoutedEventArgs e)
        {
            ActivateOrOpenRecurring();
        }

        public void ActivateOrOpenRecurringPublic() => ActivateOrOpenRecurring();

        public void RefreshHome() => _homeView.Refresh();

        private void ActivateOrOpenRecurring()
        {
            const string tabHeader = "Recurring Transactions";
            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == tabHeader)
                {
                    (item.Content as RecurringTransView)?.Refresh();
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }
            var view = new RecurringTransView(_db);
            AddClosableTab(tabHeader, view);
        }

        private void LoanStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new LoanSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenLoanStatement(dlg.SelectedAccount);
        }

        private void ActivateOrOpenLoanStatement(string account)
        {
            var tabHeader = $"Loan: {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (GetTabHeaderText(item) == tabHeader)
                {
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new LoanStatementView(_db, account);
            AddClosableTab(tabHeader, view);
        }

        // Refreshes any open Loan Statement / Loan Interest tabs for the given account.
        // Used after a loan payment is posted from outside the Loan Statement window
        // (e.g. via the Recurring Transactions "View" button).
        public void RefreshLoanTabs(string account)
        {
            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is LoanStatementView lsv && lsv.Account == account)
                    lsv.Refresh();
                else if (item.Content is LoanInterestView liv && liv.Account == account)
                    liv.Refresh();
            }
            _homeView.Refresh();
        }

        public void ActivateOrOpenLoanInterest(string account)
        {
            var tabHeader = $"Interest – {account}";

            foreach (var item in MainTabControl.Items.OfType<TabItem>())
            {
                if (item.Content is LoanInterestView && GetTabHeaderText(item) == tabHeader)
                {
                    MainTabControl.SelectedItem = item;
                    return;
                }
            }

            var view = new LoanInterestView(_db, account);
            AddClosableTab(tabHeader, view);
        }

        // Adds a tab with a close button. canClose (optional) is called before removal;
        // returning false cancels the close.
        public TabItem AddClosableTab(string header, UIElement content,
                                      Func<bool>? canClose = null)
        {
            var tabItem = new TabItem();

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = header,
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeBtn = new Button
            {
                Content       = "×",
                Style         = (Style)Resources["CloseButtonStyle"],
                Margin        = new Thickness(6, 0, 0, 0),
                ToolTip       = "Close tab"
            };
            closeBtn.Click += (s, args) =>
            {
                args.Handled = true;
                if (canClose == null || canClose())
                    MainTabControl.Items.Remove(tabItem);
            };

            headerPanel.Children.Add(closeBtn);
            tabItem.Header  = headerPanel;
            tabItem.Content = content;

            MainTabControl.Items.Add(tabItem);
            MainTabControl.SelectedItem = tabItem;

            return tabItem;
        }
    }
}
