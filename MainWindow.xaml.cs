using System.IO;
using FinanceApp.Data;
using FinanceApp.Dialogs;
using FinanceApp.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Linq;
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
                {
                    if (tab.Content is AccountStatementView v)
                        v.SuppressWarning(true);
                    MainTabControl.Items.Remove(tab);
                }

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
            var dirty = MainTabControl.Items
                .OfType<TabItem>()
                .Select(t => t.Content as AccountStatementView)
                .OfType<AccountStatementView>()          // strips nulls, non-nullable
                .Where(v => v.HasUnsavedChanges)
                .ToList();

            if (!dirty.Any()) return;

            // Suppress each view's deactivation warning while our dialog is shown.
            foreach (var v in dirty) v.SuppressWarning(true);

            var result = MessageBox.Show(
                "There are unsaved changes in one or more account statement tabs.\n\n" +
                "Do you want to save before exiting?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            foreach (var v in dirty) v.SuppressWarning(false);

            if (result == MessageBoxResult.Yes)
                foreach (var v in dirty) v.SaveChanges();
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

                // Activate existing statement tab (refreshed) or open a new one
                ActivateOrOpenStatement(accountName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n\n{ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            if (added) RefreshAllStatementCategories();
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
                RefreshAllStatementCategories();
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

        private void AccountStatement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AccountSelectDialog(_db) { Owner = this };
            if (dlg.ShowDialog() != true) return;

            ActivateOrOpenStatement(dlg.SelectedAccount);
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
