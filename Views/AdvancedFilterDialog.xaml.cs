using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HttpTrafficMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace HttpTrafficMonitor.Views
{
    public partial class AdvancedFilterDialog : FluentWindow
    {
        public AdvancedFilterViewModel ViewModel => (AdvancedFilterViewModel)DataContext;

        public AdvancedFilterDialog()
        {
            InitializeComponent();
        }

        public AdvancedFilterDialog(AdvancedFilterViewModel vm) : this()
        {
            DataContext = vm;
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
    }
}
