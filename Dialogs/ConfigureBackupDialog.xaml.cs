using FinanceApp.Data;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class ConfigureBackupDialog : Window
    {
        private readonly DatabaseManager _db;

        public ConfigureBackupDialog(DatabaseManager db)
        {
            InitializeComponent();
            _db = db;

            var (backupOnClose, backupFolder) = _db.GetBackupSettings();
            ChkBackupOnClose.IsChecked = backupOnClose;
            TxtBackupFolder.Text = backupFolder ?? "";
            BtnBackupFolder.IsEnabled = backupOnClose;
        }

        private void ChkBackupOnClose_CheckedChanged(object sender, RoutedEventArgs e)
        {
            BtnBackupFolder.IsEnabled = ChkBackupOnClose.IsChecked == true;
        }

        private void BtnBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Backup Folder"
            };
            if (!string.IsNullOrWhiteSpace(TxtBackupFolder.Text))
                dlg.InitialDirectory = TxtBackupFolder.Text;

            if (dlg.ShowDialog() == true)
                TxtBackupFolder.Text = dlg.FolderName;
        }

        private void BtnBackupNow_Click(object sender, RoutedEventArgs e)
        {
            var saveDlg = new SaveFileDialog
            {
                Title      = "Save Database Backup",
                Filter     = "Finance Database (*.fdb)|*.fdb|All Files (*.*)|*.*",
                FileName   = $"MyFinance_backup_{DateTime.Now:yyyyMMdd_HHmmss}.fdb",
                DefaultExt = ".fdb"
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

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var backupOnClose = ChkBackupOnClose.IsChecked == true;

            if (backupOnClose && string.IsNullOrWhiteSpace(TxtBackupFolder.Text))
            {
                MessageBox.Show("Please select a backup folder.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _db.SaveBackupSettings(backupOnClose,
                string.IsNullOrWhiteSpace(TxtBackupFolder.Text) ? null : TxtBackupFolder.Text);

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
