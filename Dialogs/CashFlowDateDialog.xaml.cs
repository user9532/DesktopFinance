using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class CashFlowDateDialog : Window
    {
        public DateTime StartDate { get; private set; }
        public DateTime EndDate   { get; private set; }

        public CashFlowDateDialog()
        {
            InitializeComponent();
            DpStart.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            DpEnd.SelectedDate   = DateTime.Today;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (DpStart.SelectedDate is not DateTime start)
            {
                Warn("Please select a start date.");
                return;
            }
            if (DpEnd.SelectedDate is not DateTime end)
            {
                Warn("Please select an end date.");
                return;
            }
            if (end <= start)
            {
                Warn("End date must be later than the start date.");
                return;
            }

            StartDate    = start;
            EndDate      = end;
            DialogResult = true;
        }

        private static void Warn(string msg) =>
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
