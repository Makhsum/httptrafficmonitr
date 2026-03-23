using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace HttpTrafficMonitor.Views
{
    public partial class MainWindow : FluentWindow
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ViewModel.ScrollToBottomRequested += OnScrollToBottom;

            System.Windows.MessageBox.Show(
                "WARNING: This application intercepts HTTP/HTTPS traffic by acting as a system proxy.\n\n" +
                "When HTTPS interception is active:\n" +
                "  - A root CA certificate will be installed in your system trust store\n" +
                "  - All HTTPS traffic can be read in plaintext by this application\n" +
                "  - This is a security-sensitive operation\n\n" +
                "The system proxy will be restored when you stop monitoring or close the app.\n\n" +
                "Use this tool only for development and debugging purposes.",
                "Security Notice",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void OnScrollToBottom()
        {
            if (!ViewModel.AutoScroll) return;
            if (RequestsGrid.Items.Count == 0) return;

            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    RequestsGrid.ScrollIntoView(RequestsGrid.Items[^1]);
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RequestsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid grid) return;
            ViewModel.SelectedRequests.Clear();
            foreach (var item in grid.SelectedItems.OfType<HttpRequestEntry>())
                ViewModel.SelectedRequests.Add(item);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Don't intercept clicks on buttons (min/max/close)
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != sender)
            {
                if (source is ButtonBase)
                    return;
                source = VisualTreeHelper.GetParent(source);
            }

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                try { DragMove(); } catch { }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (ViewModel.IsMonitoring)
            {
                var result = System.Windows.MessageBox.Show(
                    "Monitoring is still active. Closing will stop the proxy and restore system proxy settings.\n\nClose anyway?",
                    "Confirm Close",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (result == System.Windows.MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            ViewModel.Dispose();
        }
    }
}
