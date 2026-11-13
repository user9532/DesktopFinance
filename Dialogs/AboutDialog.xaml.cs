using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace FinanceApp.Dialogs
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                TxtVersion.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
        }

        private void Website_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the website:\n{ex.Message}", "About",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            e.Handled = true;
        }

        private void OK_Click(object sender, RoutedEventArgs e) => Close();
    }
}
