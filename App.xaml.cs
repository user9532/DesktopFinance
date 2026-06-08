using FinanceApp.Data;
using FinanceApp.Dialogs;
using System.IO;
using System.Windows;

namespace FinanceApp
{
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Prevent WPF from shutting down when the startup dialog closes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var db = new DatabaseManager();

            if (!File.Exists(db.DbPath))
            {
                var dlg = new RestoreOrNewDialog();
                if (dlg.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                if (!string.IsNullOrEmpty(dlg.BackupSourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(db.DbPath)!);
                    File.Copy(dlg.BackupSourcePath, db.DbPath, overwrite: false);
                }
                // Empty path → start fresh; InitializeDatabase creates the schema.
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new MainWindow();
            window.Show();
        }
    }
}

