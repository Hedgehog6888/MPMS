using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MPMS.Infrastructure;

/// <summary>
/// Visibility converter. Use Instance for bool→Visible/Collapsed.
/// Use NotEmpty for string→Visible/Collapsed (hidden when empty).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance  = new();
    public static readonly BoolToVisibilityConverter NotEmpty  = new() { IsStringMode = true };

    public bool IsStringMode { get; init; }
    public bool Invert { get; init; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag;
        if (IsStringMode)
            flag = value is string s && !string.IsNullOrEmpty(s);
        else
            flag = value is true;

        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
