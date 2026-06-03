using System.Windows;

namespace FinanceApp.Views
{
    // Relays a DataContext value into DataGrid template columns where
    // RelativeSource/ElementName bindings lose the visual-tree link.
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }
    }
}
