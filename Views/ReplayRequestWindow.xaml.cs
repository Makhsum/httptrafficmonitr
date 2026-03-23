using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace HttpTrafficMonitor.Views
{
    public partial class ReplayRequestWindow : FluentWindow
    {
        public ReplayRequestViewModel ViewModel => (ReplayRequestViewModel)DataContext;

        public ReplayRequestWindow()
        {
            InitializeComponent();
        }

        public ReplayRequestWindow(HttpRequestEntry? sourceRequest) : this()
        {
            if (sourceRequest != null)
                ViewModel.LoadFromRequest(sourceRequest);
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

        protected override void OnClosed(System.EventArgs e)
        {
            ViewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
