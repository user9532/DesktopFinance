using System.Windows;

namespace FinanceApp.Dialogs
{
    public partial class AccountTypeDialog : Window
    {
        public NewAccountType SelectedType { get; private set; }

        public AccountTypeDialog() => InitializeComponent();

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            SelectedType = RbCreditCard.IsChecked     is true ? NewAccountType.CreditCard  :
                           RbInvestment.IsChecked     is true ? NewAccountType.Investment   :
                           RbOtherAsset.IsChecked     is true ? NewAccountType.OtherAsset   :
                           RbLoan.IsChecked           is true ? NewAccountType.Loan         :
                           RbOtherLiability.IsChecked is true ? NewAccountType.OtherLiability :
                                                                NewAccountType.Bank;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
