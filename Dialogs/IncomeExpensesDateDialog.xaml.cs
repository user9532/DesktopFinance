using System;
using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class IncomeExpensesDateDialog : Window
    {
        public DateTime StartDate { get; private set; }
        public DateTime EndDate   { get; private set; }

        public IncomeExpensesDateDialog()
        {
            InitializeComponent();
        }

        private void DatePicker_SelectedDateChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e) => Validate();

        private void Validate()
        {
            if (TbError == null || BtnOk == null) return;   // called before InitializeComponent finishes

            var start = DpStart.SelectedDate;
            var end   = DpEnd.SelectedDate;

            if (start == null || end == null)
            {
                TbError.Visibility = Visibility.Collapsed;
                BtnOk.IsEnabled    = false;
                return;
            }

            if (end <= start)
            {
                TbError.Text       = "End date must be later than start date.";
                TbError.Visibility = Visibility.Visible;
                BtnOk.IsEnabled    = false;
                return;
            }

            TbError.Visibility = Visibility.Collapsed;
            BtnOk.IsEnabled    = true;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            StartDate    = DpStart.SelectedDate!.Value;
            EndDate      = DpEnd.SelectedDate!.Value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
