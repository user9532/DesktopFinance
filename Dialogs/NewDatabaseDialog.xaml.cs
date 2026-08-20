using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class NewDatabaseDialog : Window
    {
        public NewDatabaseDialog()
        {
            InitializeComponent();
        }

        private void ChkHaveBackup_CheckedChanged(object sender, RoutedEventArgs e)
        {
            BtnContinue.IsEnabled = ChkHaveBackup.IsChecked == true;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
