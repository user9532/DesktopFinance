using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FinanceApp.Views
{
    public class TransactionRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ── identity / metadata ───────────────────────────────────────────
        public long   RowId              { get; set; }
        public bool   IsNew              { get; set; }
        public bool   MatchedViaCategory { get; set; }
        public bool   NameMatchedCatHistory { get; set; }
        public string AccountValue       { get; set; } = "";   // DB Account column
        public string OriginalCategory   { get; set; } = "";   // category at load time
        public bool   IsDirty            { get; set; }

        private bool _loading;
        public void BeginLoad() => _loading = true;
        public void EndLoad()   => _loading = false;
        private void Touch() { if (!_loading) IsDirty = true; }

        // ── editable display fields ───────────────────────────────────────

        private DateTime? _dateValue;
        public DateTime? DateValue
        {
            get => _dateValue;
            set { _dateValue = value; Notify(); Notify(nameof(DateDisplay)); Touch(); }
        }
        public string DateDisplay => _dateValue?.ToString("yyyy-MM-dd") ?? "";

        private string _checkNo = "";
        public string CheckNo
        {
            get => _checkNo;
            set { _checkNo = value; Notify(); Touch(); }
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; Notify(); Touch(); }
        }

        private string _displayCategory = "";
        public string DisplayCategory
        {
            get => _displayCategory;
            set { _displayCategory = value; Notify(); Touch(); }
        }

        private bool _clear;
        public bool Clear
        {
            get => _clear;
            set { _clear = value; Notify(); Touch(); }
        }

        private string _payment = "";
        public string Payment
        {
            get => _payment;
            set { _payment = value; Notify(); Touch(); }
        }

        private string _deposit = "";
        public string Deposit
        {
            get => _deposit;
            set { _deposit = value; Notify(); Touch(); }
        }

        private decimal _balance;
        public decimal Balance
        {
            get => _balance;
            set { _balance = value; Notify(); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { _memo = value; Notify(); Touch(); }
        }

        private string _financialId = "";
        public string FinancialId
        {
            get => _financialId;
            set { _financialId = value; Notify(); Touch(); }
        }

        // TrnType as stored in DB — used when saving to unswap category-matched rows
        public string TrnType { get; set; } = "";

        // ── helpers ───────────────────────────────────────────────────────

        public decimal PaymentDecimal =>
            decimal.TryParse(_payment, out var d) ? d : 0m;

        public decimal DepositDecimal =>
            decimal.TryParse(_deposit, out var d) ? d : 0m;
    }
}
