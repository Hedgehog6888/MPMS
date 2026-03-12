using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

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

/// <summary>Converts value to bool: true when value equals parameter (string comparison).</summary>
public class EqualityToBoolConverter : IValueConverter
{
    public static readonly EqualityToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Converts a hex color string (e.g. "#C0392B") to a SolidColorBrush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch { /* fall through */ }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a TaskStatus enum to a SolidColorBrush for UI display.</summary>
public class TaskStatusToBrushConverter : IValueConverter
{
    public static readonly TaskStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskStatus.Planned    => new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)),
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)),
            TaskStatus.Paused     => new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x00)),
            TaskStatus.Completed  => new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)),
            _                     => new SolidColorBrush(Colors.Gray)
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a ProjectStatus enum to a SolidColorBrush.</summary>
public class ProjectStatusToBrushConverter : IValueConverter
{
    public static readonly ProjectStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ProjectStatus.Planning   => new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)),
            ProjectStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)),
            ProjectStatus.Completed  => new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)),
            ProjectStatus.Cancelled  => new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)),
            _                        => new SolidColorBrush(Colors.Gray)
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a TaskPriority enum to a SolidColorBrush.</summary>
public class PriorityToBrushConverter : IValueConverter
{
    public static readonly PriorityToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskPriority.Low      => new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)),
            TaskPriority.Medium   => new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)),
            TaskPriority.High     => new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x00)),
            TaskPriority.Critical => new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)),
            _                     => new SolidColorBrush(Colors.Gray)
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a StageStatus enum to a SolidColorBrush.</summary>
public class StageStatusToBrushConverter : IValueConverter
{
    public static readonly StageStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            StageStatus.Planned    => new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)),
            StageStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)),
            StageStatus.Completed  => new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)),
            _                      => new SolidColorBrush(Colors.Gray)
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts TaskStatus enum to Russian display string.</summary>
public class TaskStatusToStringConverter : IValueConverter
{
    public static readonly TaskStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskStatus.Planned    => "Запланирована",
            TaskStatus.InProgress => "Выполняется",
            TaskStatus.Paused     => "Приостановлена",
            TaskStatus.Completed  => "Завершена",
            _                     => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts ProjectStatus enum to Russian display string.</summary>
public class ProjectStatusToStringConverter : IValueConverter
{
    public static readonly ProjectStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ProjectStatus.Planning   => "Планирование",
            ProjectStatus.InProgress => "В работе",
            ProjectStatus.Completed  => "Завершён",
            ProjectStatus.Cancelled  => "Отменён",
            _                        => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts TaskPriority enum to Russian display string.</summary>
public class PriorityToStringConverter : IValueConverter
{
    public static readonly PriorityToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskPriority.Low      => "Низкий",
            TaskPriority.Medium   => "Средний",
            TaskPriority.High     => "Высокий",
            TaskPriority.Critical => "Критический",
            _                     => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts StageStatus enum to Russian display string.</summary>
public class StageStatusToStringConverter : IValueConverter
{
    public static readonly StageStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            StageStatus.Planned    => "Запланирован",
            StageStatus.InProgress => "Выполняется",
            StageStatus.Completed  => "Завершён",
            _                      => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts DateOnly? to a display string for WPF binding.</summary>
public class DateOnlyToStringConverter : IValueConverter
{
    public static readonly DateOnlyToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateOnly d) return d.ToString("dd.MM.yyyy");
        return "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts DateOnly? to DateTime? for WPF DatePicker two-way binding.</summary>
public class DateOnlyToDateTimeConverter : IValueConverter
{
    public static readonly DateOnlyToDateTimeConverter Instance = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateOnly d) return d.ToDateTime(TimeOnly.MinValue);
        return null;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        return null;
    }
}
