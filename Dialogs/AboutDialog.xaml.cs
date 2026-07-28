using System.Reflection;
using System.Windows;

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

        private void OK_Click(object sender, RoutedEventArgs e) => Close();
    }
}
