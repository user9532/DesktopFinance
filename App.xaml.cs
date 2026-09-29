using FinanceApp.Data;
using FinanceApp.Dialogs;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FinanceApp
{
    public partial class App : Application
    {
        // Held for the lifetime of the process; a second instance sees the mutex
        // already owned (createdNew = false) and exits instead of running alongside
        // the first, which would otherwise let two processes write the same SQLite
        // database file concurrently.
        private static Mutex? _singleInstanceMutex;

        private void App_Startup(object sender, StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, "DesktopFinance-SingleInstance-9F1B2C3D-6E4A-4B8F-9C2D-7A1E3F5B8C6D", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "DesktopFinance is already running.",
                    "DesktopFinance", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += App_UnhandledException;

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

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogAndShow(e.Exception);
            e.Handled = true;
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) LogAndShow(ex);
        }

        private static void LogAndShow(Exception ex)
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DesktopFinance", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.Now}: {ex}\n\n");
            }
            catch { /* logging is best-effort */ }

            MessageBox.Show(
                $"An unexpected error occurred:\n\n{ex.Message}\n\nDetails were logged.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

