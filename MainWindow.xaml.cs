using FinanceApp.Data;
using FinanceApp.Dialogs;
using FinanceApp.Views;
using Microsoft.Win32;
using System;
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

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
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

                // Open Account Statement tab for the imported account
                var view = new AccountStatementView(_db, accountName);
                AddClosableTab($"Statement: {accountName}", view, () => view.CanClose());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n\n{ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewAccount_Click(object sender, RoutedEventArgs e)
        {
            var typeDialog = new AccountTypeDialog { Owner = this };
            if (typeDialog.ShowDialog() != true) return;

            switch (typeDialog.SelectedType)
            {
                case NewAccountType.Bank:
                case NewAccountType.CreditCard:
                case NewAccountType.Investment:
                    new StandardAccountDialog(_db, typeDialog.SelectedType) { Owner = this }.ShowDialog();
                    break;

                case NewAccountType.Loan:
                    new LoanAccountDialog(_db) { Owner = this }.ShowDialog();
                    break;

                case NewAccountType.OtherAsset:
                case NewAccountType.OtherLiability:
                    new OtherAccountDialog(_db, typeDialog.SelectedType) { Owner = this }.ShowDialog();
                    break;
            }
        }

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            new NewCategoryDialog(_db) { Owner = this }.ShowDialog();
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

            var account = dlg.SelectedAccount;
            var view    = new AccountStatementView(_db, account);

            AddClosableTab($"Statement: {account}", view, () => view.CanClose());
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
