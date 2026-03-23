using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace HttpTrafficMonitor.Views
{
    public partial class ComparisonWindow : FluentWindow
    {
        public ComparisonViewModel ViewModel => (ComparisonViewModel)DataContext;

        public ComparisonWindow()
        {
            InitializeComponent();
        }

        public ComparisonWindow(HttpRequestEntry a, HttpRequestEntry b) : this()
        {
            ViewModel.LoadRequests(a, b);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null && source != sender)
            {
                if (source is ButtonBase) return;
                source = VisualTreeHelper.GetParent(source);
            }

            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                try { DragMove(); } catch { }
        }

        private void DiffTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tab)
                ViewModel.SelectedTab = tab;
        }
    }
}
