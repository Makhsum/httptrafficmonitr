using System;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using HttpTrafficMonitor.Services;
using HttpTrafficMonitor.ViewModels;
using Wpf.Ui.Appearance;

namespace HttpTrafficMonitor
{
    public partial class App : Application
    {
        private IpcApiService? _ipcApi;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handlers to ensure proxy cleanup
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "HTTP Traffic Monitor requires Administrator privileges to:\n\n" +
                    "  - Set the system proxy for traffic interception\n" +
                    "  - Install a root certificate for HTTPS decryption\n" +
                    "  - Access process information for network connections\n\n" +
                    "Please restart the application as Administrator.",
                    "Administrator Privileges Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Shutdown(1);
                return;
            }

            // Apply default dark theme
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.Mica);

            // Start IPC API after main window loads
            Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow?.DataContext is MainViewModel vm)
                {
                    _ipcApi = new IpcApiService(vm);
                    _ipcApi.Start();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CleanupProxy();
            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            CleanupProxy();
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            CleanupProxy();
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe system proxy has been restored.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CleanupProxy()
        {
            try
            {
                _ipcApi?.Dispose();
                _ipcApi = null;
            }
            catch { }

            try
            {
                if (MainWindow?.DataContext is MainViewModel vm)
                {
                    vm.ExecuteStop();
                    vm.Dispose();
                }
            }
            catch { }
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
