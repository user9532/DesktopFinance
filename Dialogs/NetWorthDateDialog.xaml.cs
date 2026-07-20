using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class NetWorthDateDialog : Window
    {
        public DateTime SelectedDate { get; private set; } = DateTime.Today;

        public NetWorthDateDialog()
        {
            InitializeComponent();
            DpAsOf.SelectedDate = DateTime.Today;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (DpAsOf.SelectedDate is not DateTime d)
            {
                MessageBox.Show("Please select a date.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedDate = d;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
