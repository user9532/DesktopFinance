using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class RestoreOrNewDialog : Window
    {
        // Null  → user cancelled (neither button confirmed a path)
        // Empty → user chose "Start New"
        // Path  → user picked a backup file to restore from
        public string? BackupSourcePath { get; private set; }

        public RestoreOrNewDialog()
        {
            InitializeComponent();
        }

        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select Backup File",
                Filter = "Finance Database (*.fdb)|*.fdb|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            BackupSourcePath = dlg.FileName;
            DialogResult = true;
        }

        private void NewBtn_Click(object sender, RoutedEventArgs e)
        {
            BackupSourcePath = string.Empty;
            DialogResult = true;
        }
    }
}
