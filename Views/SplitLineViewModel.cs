using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FinanceApp.Views
{
    public class SplitLineViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private string _category = "";
        public string Category
        {
            get => _category;
            set { _category = value; Notify(); }
        }

        private string _amount = "";
        public string Amount
        {
            get => _amount;
            set { _amount = value; Notify(); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { _memo = value; Notify(); }
        }

        public decimal AmountDecimal =>
            decimal.TryParse(_amount, NumberStyles.Any, CultureInfo.CurrentCulture, out var d) ? d : 0m;
    }
}
