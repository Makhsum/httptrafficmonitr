using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HttpTrafficMonitor.Converters
{
    public class MethodToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string method) return Brushes.Gray;
            return method.ToUpperInvariant() switch
            {
                "GET" => new SolidColorBrush(Color.FromRgb(0, 120, 212)),     // Blue (Accent)
                "POST" => new SolidColorBrush(Color.FromRgb(16, 124, 16)),    // Green (Success)
                "PUT" => new SolidColorBrush(Color.FromRgb(157, 93, 0)),      // Orange (Caution)
                "PATCH" => new SolidColorBrush(Color.FromRgb(107, 69, 186)),  // Purple
                "DELETE" => new SolidColorBrush(Color.FromRgb(196, 43, 28)),  // Red (Danger)
                "OPTIONS" => new SolidColorBrush(Color.FromRgb(0, 133, 155)), // Teal
                "HEAD" => new SolidColorBrush(Color.FromRgb(118, 118, 118)),  // Gray
                _ => Brushes.Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusCodeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int code) return Brushes.Gray;
            return code switch
            {
                >= 200 and < 300 => new SolidColorBrush(Color.FromRgb(16, 124, 16)),   // Green
                >= 300 and < 400 => new SolidColorBrush(Color.FromRgb(0, 133, 155)),   // Teal
                >= 400 and < 500 => new SolidColorBrush(Color.FromRgb(157, 93, 0)),    // Orange
                >= 500 => new SolidColorBrush(Color.FromRgb(196, 43, 28)),             // Red
                _ => Brushes.Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StatusCodeToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not int code) return Brushes.Transparent;
            return code switch
            {
                >= 200 and < 300 => new SolidColorBrush(Color.FromArgb(30, 16, 124, 16)),
                >= 300 and < 400 => new SolidColorBrush(Color.FromArgb(30, 0, 133, 155)),
                >= 400 and < 500 => new SolidColorBrush(Color.FromArgb(30, 157, 93, 0)),
                >= 500 => new SolidColorBrush(Color.FromArgb(30, 196, 43, 28)),
                _ => Brushes.Transparent
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string s && s == "invert";
            bool isNull = value == null || (value is string str && string.IsNullOrEmpty(str));
            if (invert) return isNull ? Visibility.Visible : Visibility.Collapsed;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string s && s == "invert";
            bool boolVal = value is bool b && b;
            if (invert) boolVal = !boolVal;
            return boolVal ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string s && s == "invert";
            bool isVisible = value is Visibility v && v == Visibility.Visible;
            return invert ? !isVisible : isVisible;
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
    }

    public class NullableToDashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "-";
            return value.ToString() ?? "-";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
