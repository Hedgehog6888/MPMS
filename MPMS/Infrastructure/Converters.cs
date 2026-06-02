using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Infrastructure;

/// <summary>
/// Конвертер видимости. Instance для bool→Visible/Collapsed.
/// NotEmpty для string→Visible/Collapsed (скрыто когда пусто).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public static readonly BoolToVisibilityConverter NotEmpty = new() { IsStringMode = true };

    public bool IsStringMode { get; init; }
    public bool Invert { get; init; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag;
        if (IsStringMode)
            flag = value is string s && !string.IsNullOrEmpty(s);
        else if (value is int count)
            flag = count > 0;
        else
            flag = value is true;

        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует строку в Visibility: Visible когда строка равна "───" (разделитель).</summary>
public class SeparatorToVisibilityConverter : IValueConverter
{
    public static readonly SeparatorToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s == "───")
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует строку в Visibility: Collapsed когда строка равна "───" (разделитель), иначе Visible.</summary>
public class SeparatorToVisibilityInvertedConverter : IValueConverter
{
    public static readonly SeparatorToVisibilityInvertedConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s == "───")
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует значение в bool: true когда значение равно параметру (строковое сравнение).</summary>
public class EqualityToBoolConverter : IValueConverter
{
    public static readonly EqualityToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Конвертирует в Visibility: Visible когда значение равно параметру (строковое сравнение).</summary>
public class EqualityToVisibilityConverter : IValueConverter
{
    public static readonly EqualityToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует hex-строку цвета (например "#C0392B") в SolidColorBrush.</summary>
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

/// <summary>Конвертирует путь к аватару в ImageSource для отображения. Возвращает null если путь неверный или файл отсутствует.</summary>
public class AvatarPathToImageSourceConverter : IValueConverter
{
    public static readonly AvatarPathToImageSourceConverter Instance = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует массив байтов аватара (PNG в БД) в ImageSource для отображения.
/// Возвращает null если данные null или пустые — вызывающий показывает круг с инициалами.
/// </summary>
public class AvatarBytesToImageSourceConverter : IValueConverter
{
    public static readonly AvatarBytesToImageSourceConverter Instance = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = value as byte[];
        int decodeWidth = 0;

        if (parameter != null && int.TryParse(parameter.ToString(), out int width))
        {
            decodeWidth = width;
        }

        return MPMS.Services.AvatarHelper.BytesToBitmapImage(bytes, decodeWidth);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует bool IsBlocked в локализованную строку статуса: "Активен" / "Заблокирован".
/// </summary>
public class BlockedToStatusStringConverter : IValueConverter
{
    public static readonly BlockedToStatusStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Заблокирован" : "Активен";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует bool IsBlocked в SolidColorBrush:
/// true → красный (#EF4444), false → зелёный (#22C55E).
/// </summary>
public class BlockedToStatusBrushConverter : IValueConverter
{
    public static readonly BlockedToStatusBrushConverter Instance = new();

    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush BlockedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? BlockedBrush : ActiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует AdminActionKind в локализованную русскую метку для истории.
/// </summary>
public class ActionKindToLabelConverter : IValueConverter
{
    public static readonly ActionKindToLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            MPMS.Models.ActivityActionKind.Created => "Создан",
            MPMS.Models.ActivityActionKind.Updated => "Изменён",
            MPMS.Models.ActivityActionKind.Deleted => "Удалён",
            MPMS.Models.ActivityActionKind.MarkedForDeletion => "Пометка удаления",
            MPMS.Models.ActivityActionKind.UnmarkedForDeletion => "Снята пометка",
            MPMS.Models.ActivityActionKind.Message => "Сообщение",
            MPMS.Models.ActivityActionKind.Login => "Вход",
            MPMS.Models.ActivityActionKind.Logout => "Выход",
            MPMS.Models.ActivityActionKind.PasswordChanged => "Смена пароля",
            MPMS.Models.ActivityActionKind.AvatarChanged => "Смена аватара",
            MPMS.Models.ActivityActionKind.UserCreated => "Создан пользователь",
            MPMS.Models.ActivityActionKind.UserEdited => "Изменён пользователь",
            MPMS.Models.ActivityActionKind.UserBlocked => "Заблокирован",
            MPMS.Models.ActivityActionKind.UserUnblocked => "Разблокирован",
            MPMS.Models.ActivityActionKind.UserDeleted => "Удалён пользователь",
            MPMS.Models.ActivityActionKind.Restored => "Восстановлен",
            MPMS.Models.ActivityActionKind.PermanentlyDeleted => "Удалён навсегда",
            _ => value?.ToString() ?? "—"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует AdminActionKind в SolidColorBrush для бейджей истории.
/// </summary>
public class ActionKindToBrushConverter : IValueConverter
{
    public static readonly ActionKindToBrushConverter Instance = new();

    private static readonly SolidColorBrush BlueBrush = new(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(0x54, 0x74, 0xA6));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(0x64, 0x74, 0x8B));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            MPMS.Models.ActivityActionKind.Created or
            MPMS.Models.ActivityActionKind.UserCreated => BlueBrush,

            MPMS.Models.ActivityActionKind.Login or
            MPMS.Models.ActivityActionKind.UnmarkedForDeletion or
            MPMS.Models.ActivityActionKind.Restored or
            MPMS.Models.ActivityActionKind.UserUnblocked => GreenBrush,

            MPMS.Models.ActivityActionKind.Deleted or
            MPMS.Models.ActivityActionKind.PermanentlyDeleted or
            MPMS.Models.ActivityActionKind.UserDeleted or
            MPMS.Models.ActivityActionKind.UserBlocked => RedBrush,

            MPMS.Models.ActivityActionKind.MarkedForDeletion or
            MPMS.Models.ActivityActionKind.PasswordChanged or
            MPMS.Models.ActivityActionKind.AvatarChanged or
            MPMS.Models.ActivityActionKind.Updated or
            MPMS.Models.ActivityActionKind.UserEdited => OrangeBrush,

            MPMS.Models.ActivityActionKind.Message => PurpleBrush,

            _ => GrayBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует TaskStatus в SolidColorBrush для отображения в UI.</summary>
public class TaskStatusToBrushConverter : IValueConverter
{
    public static readonly TaskStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskStatus.Planned => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            TaskStatus.Paused => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            TaskStatus.Completed => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует ProjectStatus в SolidColorBrush.</summary>
public class ProjectStatusToBrushConverter : IValueConverter
{
    public static readonly ProjectStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ProjectStatus.Planning => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            ProjectStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            ProjectStatus.Completed => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            ProjectStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            ProjectStatus.Closed => new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует TaskPriority в SolidColorBrush.</summary>
public class PriorityToBrushConverter : IValueConverter
{
    public static readonly PriorityToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskPriority.Low => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            TaskPriority.Medium => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            TaskPriority.High => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            TaskPriority.Critical => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует StageStatus в SolidColorBrush.</summary>
public class StageStatusToBrushConverter : IValueConverter
{
    public static readonly StageStatusToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            StageStatus.Planned => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            StageStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            StageStatus.Completed => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            _ => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует TaskStatus в строку отображения на русском.</summary>
public class TaskStatusToStringConverter : IValueConverter
{
    public static readonly TaskStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskStatus.Planned => "Запланирована",
            TaskStatus.InProgress => "Выполняется",
            TaskStatus.Paused => "Приостановлена",
            TaskStatus.Completed => "Завершена",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует ProjectStatus в строку отображения на русском.</summary>
public class ProjectStatusToStringConverter : IValueConverter
{
    public static readonly ProjectStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            ProjectStatus.Planning => "Планирование",
            ProjectStatus.InProgress => "Выполняется",
            ProjectStatus.Completed => "Завершён",
            ProjectStatus.Cancelled => "Отменён",
            ProjectStatus.Closed => "Закрытый",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует TaskPriority в строку отображения на русском.</summary>
public class PriorityToStringConverter : IValueConverter
{
    public static readonly PriorityToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            TaskPriority.Low => "Низкий",
            TaskPriority.Medium => "Средний",
            TaskPriority.High => "Высокий",
            TaskPriority.Critical => "Критический",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует StageStatus в строку отображения на русском.</summary>
public class StageStatusToStringConverter : IValueConverter
{
    public static readonly StageStatusToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            StageStatus.Planned => "Запланирован",
            StageStatus.InProgress => "Выполняется",
            StageStatus.Completed => "Завершён",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует DateOnly? в строку отображения для WPF привязки.</summary>
public class DateOnlyToStringConverter : IValueConverter
{
    private static readonly System.Globalization.CultureInfo RuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public static readonly DateOnlyToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateOnly d) return "—";
        string fmt = parameter as string ?? "short";
        return fmt switch
        {
            "long" => d.ToString("d MMMM yyyy", RuCulture),
            "dayname" => d.DayOfWeek switch
            {
                DayOfWeek.Monday => "понедельник",
                DayOfWeek.Tuesday => "вторник",
                DayOfWeek.Wednesday => "среда",
                DayOfWeek.Thursday => "четверг",
                DayOfWeek.Friday => "пятница",
                DayOfWeek.Saturday => "суббота",
                DayOfWeek.Sunday => "воскресенье",
                _ => d.DayOfWeek.ToString()
            },
            _ => d.ToString("dd.MM.yyyy")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует int в GridLength с единицей Star (например 5 -> 5*). Возвращает 0* если значение 0 или неверно.</summary>
public class IntToStarGridLengthConverter : IValueConverter
{
    public static readonly IntToStarGridLengthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && i > 0) return new GridLength(i, GridUnitType.Star);
        return new GridLength(0.0001, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует bool в GridLength. Параметр: "trueWidth,falseWidth" (например "220,64").</summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public static readonly BoolToGridLengthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isExpanded = value is true;
        if (parameter is string param)
        {
            var parts = param.Split(',');
            if (parts.Length == 2 && double.TryParse(parts[0], out double trueWidth) && double.TryParse(parts[1], out double falseWidth))
            {
                return new GridLength(isExpanded ? trueWidth : falseWidth, GridUnitType.Pixel);
            }
        }
        return new GridLength(isExpanded ? 220 : 64, GridUnitType.Pixel);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Анимирует значения GridLength для плавных переходов ширины колонок.</summary>
public class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction EasingFunction
    {
        get => (IEasingFunction)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override Type TargetPropertyType => typeof(GridLength);

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        if (animationClock == null || !animationClock.CurrentProgress.HasValue)
            return From;

        double progress = animationClock.CurrentProgress.Value;
        if (EasingFunction != null)
            progress = EasingFunction.Ease(progress);

        double fromValue = From.Value;
        double toValue = To.Value;
        double currentValue = fromValue + (toValue - fromValue) * progress;

        return new GridLength(currentValue, GridUnitType.Pixel);
    }
}

/// <summary>Конвертирует int в bool: true если значение > 0.</summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroConverter Instance = new();
    public bool Invert { get; init; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is int i && i > 0;
        if (Invert) result = !result;
        if (targetType == typeof(Visibility))
            return result ? Visibility.Visible : Visibility.Collapsed;
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует int (количество этапов) в локализованную строку типа "3 этапа".</summary>
public class StageCountToStringConverter : IValueConverter
{
    public static readonly StageCountToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int count) return "0 этапов";
        return count switch
        {
            0 => "0 этапов",
            1 => "1 этап",
            2 or 3 or 4 => $"{count} этапа",
            _ => $"{count} этапов"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует DateTime в относительную или форматированную строку времени.</summary>
public class DateTimeToRelativeConverter : IValueConverter
{
    public static readonly DateTimeToRelativeConverter Instance = new();

    /// <summary>В приложении в БД хранится UTC; SQLite/EF часто отдаёт Unspecified — считаем такие значения UTC.</summary>
    internal static DateTime ToLocalTimeForDisplay(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt.ToLocalTime(),
        DateTimeKind.Local => dt,
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return "";
        var local = ToLocalTimeForDisplay(dt);
        var diff = DateTime.Now - local;
        var ru = new System.Globalization.CultureInfo("ru-RU");
        var dateStr = local.ToString("d MMM", ru);

        if (diff.TotalMinutes < 1) return $"{dateStr}, только что";
        if (diff.TotalMinutes < 60) return $"{dateStr}, {(int)diff.TotalMinutes} мин. назад";
        if (local.Date == DateTime.Today) return $"{dateStr}, {local:HH:mm}";
        if (diff.TotalDays < 7) return local.ToString("d MMM, HH:mm", ru);
        return local.ToString("dd MMM yyyy, HH:mm", ru);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Для привязок с StringFormat: значения из SQLite приходят как Unspecified (фактически UTC).
/// После конвертации StringFormat даёт часы/дату уже в локальном поясе пользователя.
/// </summary>
public class UtcToLocalDateTimeConverter : IValueConverter
{
    public static readonly UtcToLocalDateTimeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            DateTime dt => DateTimeToRelativeConverter.ToLocalTimeForDisplay(dt),
            null => string.Empty,
            _ => value
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует DateOnly? в DateTime? для двусторонней привязки WPF DatePicker.</summary>
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

/// <summary>Конвертирует DateTime? в строку с русской культурой.</summary>
public class DateTimeToStringConverter : IValueConverter
{
    private static readonly System.Globalization.CultureInfo RuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public static readonly DateTimeToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dt) return "—";
        var local = DateTimeToRelativeConverter.ToLocalTimeForDisplay(dt);
        string fmt = parameter as string ?? "short";
        return fmt switch
        {
            "long" => local.ToString("d MMMM yyyy", RuCulture),
            "dayname" => local.DayOfWeek switch
            {
                DayOfWeek.Monday => "понедельник",
                DayOfWeek.Tuesday => "вторник",
                DayOfWeek.Wednesday => "среда",
                DayOfWeek.Thursday => "четверг",
                DayOfWeek.Friday => "пятница",
                DayOfWeek.Saturday => "суббота",
                DayOfWeek.Sunday => "воскресенье",
                _ => local.DayOfWeek.ToString()
            },
            _ => local.ToString("dd.MM.yyyy")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Конвертирует строку требуемой роли в Visibility на основе роли текущего пользователя.
/// Параметр: список ролей через запятую, которые должны видеть Visible (например "Admin,Administrator").
/// </summary>
public class RequiredRoleToVisibilityConverter : IValueConverter
{
    public static readonly RequiredRoleToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string requiredRoles) return Visibility.Collapsed;
        var auth = App.Services?.GetService(typeof(MPMS.Services.IAuthService)) as MPMS.Services.IAuthService;
        if (auth is null) return Visibility.Collapsed;
        string userRole = auth.UserRole ?? "";
        var roles = requiredRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool matches = roles.Any(r => string.Equals(r, userRole, StringComparison.OrdinalIgnoreCase));
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует прогресс в процентах (int) в Brush: красный &lt;30%, оранжевый 30–59%, синий 60–99%, зелёный 100%.</summary>
public class ProgressPercentToBrushConverter : IValueConverter
{
    public static readonly ProgressPercentToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var pct = value is int i ? i : 0;
        return pct >= 100
            ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
            : pct >= 60
                ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                : pct >= 30
                    ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет строку EntityType с акцентным SolidColorBrush для элементов лога активности.</summary>
public class EntityTypeToAccentBrushConverter : IValueConverter
{
    public static readonly EntityTypeToAccentBrushConverter Instance = new();

    private static readonly SolidColorBrush ProjectBrush = new(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush TaskBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush StageBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush MaterialBrush = new(Color.FromRgb(0x0D, 0x94, 0x88));
    private static readonly SolidColorBrush EquipmentBrush = new(Color.FromRgb(0x0D, 0x94, 0x88));
    private static readonly SolidColorBrush FileBrush = new(Color.FromRgb(0xF4, 0x3F, 0x5E));
    private static readonly SolidColorBrush MessageBrush = new(Color.FromRgb(0x54, 0x74, 0xA6));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x64, 0x74, 0x8B));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            "Project" => ProjectBrush,
            "Task" => TaskBrush,
            "Stage" => StageBrush,
            "TaskStage" => StageBrush,
            "Material" => MaterialBrush,
            "Equipment" => EquipmentBrush,
            "File" => FileBrush,
            "Message" => MessageBrush,
            "User" => DefaultBrush,
            _ => DefaultBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет LocalActivityLog с акцентным Brush — предпочитает ActionType (Deleted, MarkedForDeletion и т.д.) над EntityType.</summary>
public class ActivityLogToAccentBrushConverter : IValueConverter
{
    public static readonly ActivityLogToAccentBrushConverter Instance = new();

    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x64, 0x74, 0x8B));

    // === PROJECT (синяя гамма) ===
    private static readonly Color ProjectCreated = Color.FromRgb(0x1E, 0x40, 0xAF);      // тёмно-синий
    private static readonly Color ProjectUpdated = Color.FromRgb(0x25, 0x63, 0xEB);      // синий
    private static readonly Color ProjectDeleted = Color.FromRgb(0x3B, 0x82, 0xF6);      // голубой
    private static readonly Color ProjectWarning = Color.FromRgb(0x60, 0xA5, 0xFA);     // светло-голубой
    private static readonly Color ProjectClosed = Color.FromRgb(0x37, 0x41, 0x51);      // тёмно-серый

    // === TASK (зелёная гамма) ===
    private static readonly Color TaskCreated = Color.FromRgb(0x05, 0x96, 0x69);         // тёмно-зелёный
    private static readonly Color TaskUpdated = Color.FromRgb(0x10, 0xB9, 0x81);         // зелёный
    private static readonly Color TaskDeleted = Color.FromRgb(0x34, 0xD3, 0x99);         // светло-зелёный
    private static readonly Color TaskWarning = Color.FromRgb(0x6E, 0xE7, 0xB7);           // мятный

    // === STAGE (красная гамма) ===
    private static readonly Color StageCreated = Color.FromRgb(0x99, 0x1B, 0x1B);       // тёмно-красный
    private static readonly Color StageUpdated = Color.FromRgb(0xDC, 0x26, 0x26);         // красный
    private static readonly Color StageDeleted = Color.FromRgb(0xEF, 0x44, 0x44);         // ярко-красный
    private static readonly Color StageWarning = Color.FromRgb(0xF8, 0x71, 0x71);         // светло-красный

    // === MATERIAL (жёлто-оранжевая гамма) ===
    private static readonly Color MaterialCreated = Color.FromRgb(0xCA, 0x8A, 0x04);     // тёмно-жёлто-оранжевый
    private static readonly Color MaterialUpdated = Color.FromRgb(0xF5, 0x9E, 0x0B);     // жёлто-оранжевый
    private static readonly Color MaterialDeleted = Color.FromRgb(0xFB, 0x92, 0x34);     // оранжевый
    private static readonly Color MaterialWarning = Color.FromRgb(0xFD, 0xB7, 0x4C);     // светло-оранжевый

    // === EQUIPMENT (бирюзовая гамма) ===
    private static readonly Color EquipmentCreated = Color.FromRgb(0x0F, 0x76, 0x67);   // тёмно-бирюзовый
    private static readonly Color EquipmentUpdated = Color.FromRgb(0x0D, 0x94, 0x88);   // бирюзовый
    private static readonly Color EquipmentDeleted = Color.FromRgb(0x14, 0xB8, 0xA6);   // светло-бирюзовый
    private static readonly Color EquipmentWarning = Color.FromRgb(0x5E, 0xEE, 0xD4);   // очень светлый бирюзовый

    // === FILE (розовая гамма) ===
    private static readonly Color FileCreated = Color.FromRgb(0xBE, 0x18, 0x5D);          // тёмно-розовый
    private static readonly Color FileUpdated = Color.FromRgb(0xE1, 0x1D, 0x48);          // розовый
    private static readonly Color FileDeleted = Color.FromRgb(0xF4, 0x3F, 0x5E);          // розово-красный
    private static readonly Color FileWarning = Color.FromRgb(0xFB, 0x71, 0x85);          // светло-розовый

    // === IMAGE (индиго гамма) ===
    private static readonly Color ImageCreated = Color.FromRgb(0x4F, 0x46, 0xE5);       // тёмно-индиго
    private static readonly Color ImageUpdated = Color.FromRgb(0x63, 0x66, 0xF1);           // индиго
    private static readonly Color ImageDeleted = Color.FromRgb(0x81, 0x8C, 0xF8);       // светло-индиго
    private static readonly Color ImageWarning = Color.FromRgb(0xA5, 0xB4, 0xFC);        // светло-синий

    // === DOCUMENT (оливковая гамма) ===
    private static readonly Color DocumentCreated = Color.FromRgb(0x65, 0xA3, 0x0D);    // тёмно-оливковый
    private static readonly Color DocumentUpdated = Color.FromRgb(0x84, 0xCC, 0x16);    // оливковый
    private static readonly Color DocumentDeleted = Color.FromRgb(0xA3, 0xE6, 0x35);    // светло-оливковый
    private static readonly Color DocumentWarning = Color.FromRgb(0xBE, 0xF2, 0x64);    // салатовый

    // === MESSAGE (тёмно-синяя гамма) ===
    private static readonly Color MessageCreated = Color.FromRgb(0x54, 0x74, 0xA6);      // тёмно-синий

    // === USER (серая гамма) ===
    private static readonly Color UserCreated = Color.FromRgb(0x4B, 0x55, 0x63);         // тёмно-серый
    private static readonly Color UserUpdated = Color.FromRgb(0x6B, 0x72, 0x80);         // серый
    private static readonly Color UserDeleted = Color.FromRgb(0x94, 0xA3, 0xB8);         // светло-серый
    private static readonly Color UserWarning = Color.FromRgb(0xBC, 0xBF, 0xE0);         // серо-голубой

    // === SYSTEM (нейтральные цвета) ===
    private static readonly Color SystemLogin = Color.FromRgb(0x37, 0x51, 0x64);         // тёмно-синий-серый
    private static readonly Color SystemPassword = Color.FromRgb(0x05, 0x96, 0x69);      // зелёный
    private static readonly Color SystemAvatar = Color.FromRgb(0x7C, 0x3A, 0xED);        // фиолетовый

    // === STATUS CHANGED (оттенки для каждого типа сущности) ===
    private static readonly Color ProjectStatusChanged = Color.FromRgb(0x60, 0xA5, 0xFA); // светло-голубой оттенок
    private static readonly Color TaskStatusChanged = Color.FromRgb(0x6E, 0xE7, 0xB7);    // мятный оттенок
    private static readonly Color StageStatusChanged = Color.FromRgb(0xF8, 0x71, 0x71);  // светло-красный оттенок

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MPMS.Models.LocalActivityLog log)
            return DefaultBrush;

        // Отдельная логика для закрытия проекта
        if (IsProjectClosed(log))
            return new SolidColorBrush(ProjectClosed);

        // Отдельная логика для изменения статуса (оттенки для каждого типа сущности)
        if (log.ActionType == ActivityActionKind.StatusChanged)
        {
            return log.EntityType switch
            {
                "Project" => new SolidColorBrush(ProjectStatusChanged),
                "Task" => new SolidColorBrush(TaskStatusChanged),
                "Stage" => new SolidColorBrush(StageStatusChanged),
                _ => DefaultBrush
            };
        }
        if (log.ActionType == ActivityActionKind.TaskStatusChanged)
            return new SolidColorBrush(TaskStatusChanged);
        if (log.ActionType == ActivityActionKind.StageStatusChanged)
            return new SolidColorBrush(StageStatusChanged);

        var actionCategory = GetActionCategory(log.ActionType);
        var entityType = GetEntityTypeFromFileExtension(log);

        return (entityType, actionCategory) switch
        {
            // Project - синяя гамма
            (EntityType.Project, ActionCategory.Creative) => new SolidColorBrush(ProjectCreated),
            (EntityType.Project, ActionCategory.Change) => new SolidColorBrush(ProjectUpdated),
            (EntityType.Project, ActionCategory.Destructive) => new SolidColorBrush(ProjectDeleted),
            (EntityType.Project, ActionCategory.Warning) => new SolidColorBrush(ProjectWarning),

            // Task - зелёная гамма
            (EntityType.Task, ActionCategory.Creative) => new SolidColorBrush(TaskCreated),
            (EntityType.Task, ActionCategory.Change) => new SolidColorBrush(TaskUpdated),
            (EntityType.Task, ActionCategory.Destructive) => new SolidColorBrush(TaskDeleted),
            (EntityType.Task, ActionCategory.Warning) => new SolidColorBrush(TaskWarning),

            // Stage - оранжевая гамма
            (EntityType.Stage, ActionCategory.Creative) => new SolidColorBrush(StageCreated),
            (EntityType.Stage, ActionCategory.Change) => new SolidColorBrush(StageUpdated),
            (EntityType.Stage, ActionCategory.Destructive) => new SolidColorBrush(StageDeleted),
            (EntityType.Stage, ActionCategory.Warning) => new SolidColorBrush(StageWarning),

            // Material - жёлтая гамма
            (EntityType.Material, ActionCategory.Creative) => new SolidColorBrush(MaterialCreated),
            (EntityType.Material, ActionCategory.Change) => new SolidColorBrush(MaterialUpdated),
            (EntityType.Material, ActionCategory.Destructive) => new SolidColorBrush(MaterialDeleted),
            (EntityType.Material, ActionCategory.Warning) => new SolidColorBrush(MaterialWarning),

            // Equipment - бирюзовая гамма
            (EntityType.Equipment, ActionCategory.Creative) => new SolidColorBrush(EquipmentCreated),
            (EntityType.Equipment, ActionCategory.Change) => new SolidColorBrush(EquipmentUpdated),
            (EntityType.Equipment, ActionCategory.Destructive) => new SolidColorBrush(EquipmentDeleted),
            (EntityType.Equipment, ActionCategory.Warning) => new SolidColorBrush(EquipmentWarning),

            // File - розовая гамма
            (EntityType.File, ActionCategory.Creative) => new SolidColorBrush(FileCreated),
            (EntityType.File, ActionCategory.Change) => new SolidColorBrush(FileUpdated),
            (EntityType.File, ActionCategory.Destructive) => new SolidColorBrush(FileDeleted),
            (EntityType.File, ActionCategory.Warning) => new SolidColorBrush(FileWarning),

            // Image - индиго гамма
            (EntityType.Image, ActionCategory.Creative) => new SolidColorBrush(ImageCreated),
            (EntityType.Image, ActionCategory.Change) => new SolidColorBrush(ImageUpdated),
            (EntityType.Image, ActionCategory.Destructive) => new SolidColorBrush(ImageDeleted),
            (EntityType.Image, ActionCategory.Warning) => new SolidColorBrush(ImageWarning),

            // Document - оливковая гамма
            (EntityType.Document, ActionCategory.Creative) => new SolidColorBrush(DocumentCreated),
            (EntityType.Document, ActionCategory.Change) => new SolidColorBrush(DocumentUpdated),
            (EntityType.Document, ActionCategory.Destructive) => new SolidColorBrush(DocumentDeleted),
            (EntityType.Document, ActionCategory.Warning) => new SolidColorBrush(DocumentWarning),

            // Message - оранжево-коричневая гамма (один цвет для всех действий)
            (EntityType.Message, _) => new SolidColorBrush(MessageCreated),

            // User - серая гамма
            (EntityType.User, ActionCategory.Creative) => new SolidColorBrush(UserCreated),
            (EntityType.User, ActionCategory.Change) => new SolidColorBrush(UserUpdated),
            (EntityType.User, ActionCategory.Destructive) => new SolidColorBrush(UserDeleted),
            (EntityType.User, ActionCategory.Warning) => new SolidColorBrush(UserWarning),

            // System - нейтральные
            (_, ActionCategory.System) => log.ActionType switch
            {
                MPMS.Models.ActivityActionKind.Login => new SolidColorBrush(SystemLogin),
                MPMS.Models.ActivityActionKind.Logout => new SolidColorBrush(SystemLogin),
                MPMS.Models.ActivityActionKind.PasswordChanged => new SolidColorBrush(SystemPassword),
                MPMS.Models.ActivityActionKind.AvatarChanged => new SolidColorBrush(SystemAvatar),
                _ => new SolidColorBrush(SystemLogin)
            },

            // Fallbacks
            (EntityType.Project, _) => new SolidColorBrush(ProjectUpdated),
            (EntityType.Task, _) => new SolidColorBrush(TaskUpdated),
            (EntityType.Stage, _) => new SolidColorBrush(StageUpdated),
            (EntityType.Material, _) => new SolidColorBrush(MaterialUpdated),
            (EntityType.Equipment, _) => new SolidColorBrush(EquipmentUpdated),
            (EntityType.File, _) => new SolidColorBrush(FileUpdated),
            (EntityType.Image, _) => new SolidColorBrush(ImageUpdated),
            (EntityType.Document, _) => new SolidColorBrush(DocumentUpdated),
            (EntityType.User, _) => new SolidColorBrush(UserUpdated),
            (_, _) => DefaultBrush
        };
    }

    private enum ActionCategory
    {
        Destructive,
        Warning,
        Creative,
        Change,
        Communication,
        System,
        None
    }

    private enum EntityType
    {
        Project,
        Task,
        Stage,
        Material,
        Equipment,
        File,
        Image,
        Document,
        Message,
        User,
        None
    }

    private static ActionCategory GetActionCategory(string? actionType) => actionType switch
    {
        MPMS.Models.ActivityActionKind.Deleted => ActionCategory.Destructive,
        MPMS.Models.ActivityActionKind.PermanentlyDeleted => ActionCategory.Destructive,
        MPMS.Models.ActivityActionKind.UserDeleted => ActionCategory.Destructive,
        MPMS.Models.ActivityActionKind.MarkedForDeletion => ActionCategory.Warning,
        MPMS.Models.ActivityActionKind.UserBlocked => ActionCategory.Warning,
        MPMS.Models.ActivityActionKind.Created => ActionCategory.Creative,
        MPMS.Models.ActivityActionKind.UserCreated => ActionCategory.Creative,
        MPMS.Models.ActivityActionKind.Restored => ActionCategory.Creative,
        MPMS.Models.ActivityActionKind.UnmarkedForDeletion => ActionCategory.Creative,
        MPMS.Models.ActivityActionKind.UserUnblocked => ActionCategory.Creative,
        MPMS.Models.ActivityActionKind.Updated => ActionCategory.Change,
        MPMS.Models.ActivityActionKind.UserEdited => ActionCategory.Change,
        MPMS.Models.ActivityActionKind.Message => ActionCategory.Communication,
        MPMS.Models.ActivityActionKind.Login => ActionCategory.System,
        MPMS.Models.ActivityActionKind.Logout => ActionCategory.System,
        MPMS.Models.ActivityActionKind.PasswordChanged => ActionCategory.System,
        MPMS.Models.ActivityActionKind.AvatarChanged => ActionCategory.System,
        _ => ActionCategory.None
    };

    private static EntityType GetEntityType(string? entityType) => entityType switch
    {
        "Project" => EntityType.Project,
        "Task" => EntityType.Task,
        "Stage" or "TaskStage" => EntityType.Stage,
        "Material" => EntityType.Material,
        "Equipment" => EntityType.Equipment,
        "File" => EntityType.File,
        "Image" => EntityType.Image,
        "Document" => EntityType.Document,
        "Message" => EntityType.Message,
        "User" => EntityType.User,
        _ => EntityType.None
    };

    private static EntityType GetEntityTypeFromFileExtension(LocalActivityLog log)
    {
        if (log.EntityType != "File") return GetEntityType(log.EntityType);

        // Определяем тип файла по расширению из ActionText
        var fileName = log.ActionText;
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg" };
        var documentExtensions = new[] { ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp" };

        if (imageExtensions.Contains(extension))
            return EntityType.Image;
        if (documentExtensions.Contains(extension))
            return EntityType.Document;

        return EntityType.File;
    }

    private static bool IsProjectClosed(LocalActivityLog log)
    {
        return string.Equals(log.EntityType, "Project", StringComparison.Ordinal) &&
               log.ActionText.Contains("закрыт", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет LocalActivityLog с вертикальным градиентом левой полоски в ленте действий.</summary>
public class ActivityLogToGradientBrushConverter : IValueConverter
{
    public static readonly ActivityLogToGradientBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LocalActivityLog log
            ? ActivityLogStripeBrushBuilder.Build(log)
            : ActivityLogStripeBrushBuilder.Build(new LocalActivityLog());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет LocalActivityLog с Brush для панели Admin Activity — простая палитра по ActionType (login/logout/password/avatar/user actions).</summary>
public class ActivityLogToAdminActivityBrushConverter : IValueConverter
{
    public static readonly ActivityLogToAdminActivityBrushConverter Instance = new();

    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly SolidColorBrush LoginBrush = new(Color.FromRgb(0x10, 0xB9, 0x81));      // зелёный
    private static readonly SolidColorBrush LogoutBrush = new(Color.FromRgb(0x6B, 0x72, 0x80));     // серый
    private static readonly SolidColorBrush PasswordBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));  // оранжевый
    private static readonly SolidColorBrush AvatarBrush = new(Color.FromRgb(0x8B, 0x5C, 0xF6));     // фиолетовый
    private static readonly SolidColorBrush UserCreatedBrush = new(Color.FromRgb(0x25, 0x63, 0xEB)); // синий
    private static readonly SolidColorBrush UserEditedBrush = new(Color.FromRgb(0xF5, 0x9E, 0x0B));  // оранжевый
    private static readonly SolidColorBrush UserDeletedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));  // красный
    private static readonly SolidColorBrush UserBlockedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44)); // красный
    private static readonly SolidColorBrush UserUnblockedBrush = new(Color.FromRgb(0x10, 0xB9, 0x81)); // зелёный

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MPMS.Models.LocalActivityLog log)
            return DefaultBrush;

        return log.ActionType switch
        {
            MPMS.Models.ActivityActionKind.Login => LoginBrush,
            MPMS.Models.ActivityActionKind.Logout => LogoutBrush,
            MPMS.Models.ActivityActionKind.PasswordChanged => PasswordBrush,
            MPMS.Models.ActivityActionKind.AvatarChanged => AvatarBrush,
            MPMS.Models.ActivityActionKind.UserCreated => UserCreatedBrush,
            MPMS.Models.ActivityActionKind.UserEdited => UserEditedBrush,
            MPMS.Models.ActivityActionKind.UserDeleted => UserDeletedBrush,
            MPMS.Models.ActivityActionKind.UserBlocked => UserBlockedBrush,
            MPMS.Models.ActivityActionKind.UserUnblocked => UserUnblockedBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет строку EntityType с локализованной русской меткой для бейджей лога активности.</summary>
public class EntityTypeToBadgeLabelConverter : IValueConverter
{
    public static readonly EntityTypeToBadgeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            "Project" => "Проект",
            "Task" => "Задача",
            "Stage" => "Этап",
            "TaskStage" => "Этап",
            "Material" => "Материал",
            "Equipment" => "Оборудование",
            "File" => "Файл",
            "Image" => "Изображение",
            "Document" => "Документ",
            "Message" => "Сообщение",
            "User" => "Пользователь",
            _ => "—"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет ActorRole с локализованной русской меткой для бейджа роли в логе активности.</summary>
public class ActorRoleToLabelConverter : IValueConverter
{
    public static readonly ActorRoleToLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => NormalizeRoleKey(value?.ToString()) switch
        {
            "admin" => "Админ",
            "manager" => "Менеджер",
            "foreman" => "Прораб",
            "worker" => "Работник",
            _ => ""
        };

    /// <summary>Английские ключи, русские названия из сообщений (RoleToRussian), короткие формы.</summary>
    internal static string NormalizeRoleKey(string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || role == "—") return "";
        return role.Trim() switch
        {
            "Administrator" or "Admin" => "admin",
            "Project Manager" or "ProjectManager" or "Manager" => "manager",
            "Foreman" => "foreman",
            "Worker" => "worker",
            "Администратор" or "Админ" => "admin",
            "Менеджер" => "manager",
            "Прораб" => "foreman",
            "Работник" => "worker",
            _ => ""
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет ActorRole со светлой пастельной Brush для фона бейджа роли (отличается от бейджей сущностей).</summary>
public class ActorRoleToBrushConverter : IValueConverter
{
    public static readonly ActorRoleToBrushConverter Instance = new();

    private static readonly SolidColorBrush AdminBrush = new(Color.FromRgb(0xFE, 0xE2, 0xE2));
    private static readonly SolidColorBrush ManagerBrush = new(Color.FromRgb(0xDB, 0xE8, 0xFE));
    private static readonly SolidColorBrush ForemanBrush = new(Color.FromRgb(0xD1, 0xFA, 0xE5));
    private static readonly SolidColorBrush WorkerBrush = new(Color.FromRgb(0xED, 0xE9, 0xFE));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0xF1, 0xF3, 0xF5));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ActorRoleToLabelConverter.NormalizeRoleKey(value?.ToString()) switch
        {
            "admin" => AdminBrush,
            "manager" => ManagerBrush,
            "foreman" => ForemanBrush,
            "worker" => WorkerBrush,
            _ => DefaultBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет ActorRole с Brush для текста бейджа роли (тёмный акцент).</summary>
public class ActorRoleToForegroundBrushConverter : IValueConverter
{
    public static readonly ActorRoleToForegroundBrushConverter Instance = new();

    private static readonly SolidColorBrush AdminBrush = new(Color.FromRgb(0x99, 0x1B, 0x1B));
    private static readonly SolidColorBrush ManagerBrush = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush ForemanBrush = new(Color.FromRgb(0x16, 0x65, 0x34));
    private static readonly SolidColorBrush WorkerBrush = new(Color.FromRgb(0x6D, 0x28, 0xD9));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x4B, 0x55, 0x63));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ActorRoleToLabelConverter.NormalizeRoleKey(value?.ToString()) switch
        {
            "admin" => AdminBrush,
            "manager" => ManagerBrush,
            "foreman" => ForemanBrush,
            "worker" => WorkerBrush,
            _ => DefaultBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Скрывает бейдж роли, если значение не известной роли (пусто, «—», произвольный текст).</summary>
public class ActorRoleToBadgeVisibilityConverter : IValueConverter
{
    public static readonly ActorRoleToBadgeVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = ActorRoleToLabelConverter.NormalizeRoleKey(value?.ToString());
        return string.IsNullOrEmpty(key) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Проверка null/empty: Visible когда значение НЕ null/empty, иначе Collapsed.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; init; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null => false,
            byte[] b => b.Length > 0,
            string s => !string.IsNullOrEmpty(s),
            _ => true
        };
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет TaskStatus со светлой/оттенённой фоновой Brush (светлая версия цвета статуса).</summary>
public class TaskStatusToPaleBrushConverter : IValueConverter
{
    public static readonly TaskStatusToPaleBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskStatus s ? s switch
        {
            TaskStatus.Planned => new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),  // Slate-50
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),  // Blue-50
            TaskStatus.Paused => new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB)),  // Amber-50
            TaskStatus.Completed => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),  // Green-50
            _ => new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC))
        } : new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Сопоставляет TaskStatus с тёмным цветом переднего плана, соответствующим светлому фоновому бейджу.</summary>
public class TaskStatusToForegroundBrushConverter : IValueConverter
{
    public static readonly TaskStatusToForegroundBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskStatus s ? s switch
        {
            TaskStatus.Planned => new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),  // Slate-600
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),  // Blue-600
            TaskStatus.Paused => new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),  // Amber-600
            TaskStatus.Completed => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),  // Green-600
            _ => new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
        } : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует дробь 0–1 double в Star GridLength для пропорциональных колонок Timeline.</summary>
public class FractionToGridLengthConverter : IValueConverter
{
    public static readonly FractionToGridLengthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0.0;
        return new GridLength(Math.Max(0.001, fraction), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Конвертирует строку инициалов в детерминированный акцентный цвет SolidColorBrush.</summary>
public class InitialsToBrushConverter : IValueConverter
{
    private static readonly string[] Palette =
    {
        "#1ABC9C", "#2ECC71", "#3498DB", "#9B59B6", "#34495E",
        "#F1C40F", "#E67E22", "#E74C3C", "#95A5A6", "#D35400"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(s)) return new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E));
        int hash = 0;
        foreach (var c in s) hash = hash * 31 + c;
        var hex = Palette[Math.Abs(hash) % Palette.Length];
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch { return new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)); }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Текст строки выпадающего списка FormCombo: DisplayMemberPath родительского ComboBox или строка/ToString().</summary>
public sealed class FormComboItemTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not { Length: >= 2 }) return "";
        var item = values[0];
        if (item is null) return "";

        if (values[1] is ComboBox cb && !string.IsNullOrEmpty(cb.DisplayMemberPath))
        {
            var prop = item.GetType().GetProperty(
                cb.DisplayMemberPath,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
                return prop.GetValue(item)?.ToString() ?? "";
        }

        return item switch
        {
            string s => s,
            _ => item.ToString() ?? ""
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Вспомогательные штуки для шаблона FormCombo: прокрутка попапа, проверка для полосы фильтров.</summary>
public static class FormComboHelpers
{
    public static readonly DependencyProperty PopupScrollProperty =
        DependencyProperty.RegisterAttached(
            "PopupScroll",
            typeof(bool),
            typeof(FormComboHelpers),
            new PropertyMetadata(false, OnPopupScrollChanged));

    public static void SetPopupScroll(DependencyObject element, bool value)
        => element.SetValue(PopupScrollProperty, value);

    public static bool GetPopupScroll(DependencyObject element)
        => (bool)element.GetValue(PopupScrollProperty);

    private static readonly MouseWheelEventHandler FormComboDropWheelHandler = OnFormComboDropWheel;

    private static void OnPopupScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue)
            sv.AddHandler(UIElement.PreviewMouseWheelEvent, FormComboDropWheelHandler, handledEventsToo: true);
        else
            sv.RemoveHandler(UIElement.PreviewMouseWheelEvent, FormComboDropWheelHandler);
    }

    private static void OnFormComboDropWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        var max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
        if (max <= 0) return;

        var step = -e.Delta / 8.0;
        var next = Math.Clamp(sv.VerticalOffset + step, 0, max);
        sv.ScrollToVerticalOffset(next);
        e.Handled = true;
    }

    /// <summary>Не перехватывать колесо на полосе фильтра, если открыт ComboBox под курсором.</summary>
    public static bool IsMouseWheelOverOpenComboBox(MouseWheelEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject;
             d != null;
             d = VisualTreeHelper.GetParent(d))
        {
            if (d is ComboBox { IsDropDownOpen: true })
                return true;
        }

        return false;
    }

    /// <summary>Есть ли в поддереве открытый выпадающий список (для блокировки прокрутки фона).</summary>
    public static bool HasAnyDropDownOpen(DependencyObject? root)
    {
        if (root is null) return false;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ComboBox { IsDropDownOpen: true })
                return true;
            if (HasAnyDropDownOpen(child))
                return true;
        }

        return false;
    }
}

public class StringMatchToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strValue && parameter is string strParameter)
        {
            return string.Equals(strValue, strParameter, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string strParameter)
        {
            return strParameter;
        }
        return Binding.DoNothing;
    }
}

/// <summary>Сопоставляет строку FileType с локальным путём SVG иконки.</summary>
public class FileTypeToIconConverter : IValueConverter
{
    public static readonly FileTypeToIconConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? type = value?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(type)) return "/icons/file.svg";

        if (type.StartsWith("image/")) return "/icons/picture.svg";
        return "/icons/file.svg";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Возвращает true если FileType начинается с "image/".</summary>
public class FileTypeToIsImageConverter : IValueConverter
{
    public static readonly FileTypeToIsImageConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? type = value?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(type)) return false;

        if (type.StartsWith("image/")) return true;

        var ext = Path.GetExtension(type);
        if (string.IsNullOrEmpty(ext)) ext = "." + type;

        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tiff" or ".heic";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Возвращает Visibility.Visible если файл поддерживаемый тип документа для DocumentViewerOverlay.</summary>
public class FileTypeToIsDocumentConverter : IValueConverter
{
    public static readonly FileTypeToIsDocumentConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? fileName = value?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(fileName)) return Visibility.Collapsed;

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return Visibility.Collapsed;

        bool isDocument = ext is ".txt" or ".csv" or ".log" or ".json" or ".xml" or ".md" or ".html" or ".htm" or
                          ".doc" or ".docx" or ".docm" or ".dot" or ".dotx" or
                          ".xls" or ".xlsx" or ".xlsm" or ".xlsb";

        return isDocument ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Возвращает фоновый SolidColorBrush на основе расширения файла.
/// </summary>
public class FileExtensionToBackgroundBrushConverter : IValueConverter
{
    public static readonly FileExtensionToBackgroundBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = (value?.ToString() ?? "").ToLower().Trim();
        if (string.IsNullOrEmpty(input)) return new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));

        if (input.Contains("/"))
        {
            if (input.Contains("wordprocessingml") || input.Contains("msword")) input = ".docx";
            else if (input.Contains("spreadsheetml") || input.Contains("ms-excel")) input = ".xlsx";
            else if (input.Contains("pdf")) input = ".pdf";
            else if (input.Contains("image/png")) input = ".png";
            else if (input.Contains("image/jpeg")) input = ".jpg";
        }

        var ext = Path.GetExtension(input);
        if (string.IsNullOrEmpty(ext)) ext = "." + input;

        return ext switch
        {
            ".doc" or ".docx" => new SolidColorBrush(Color.FromRgb(0xEB, 0xF5, 0xFF)), // Blue-50
            ".pdf" => new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2)), // Red-50
            ".xls" or ".xlsx" or ".csv" => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)), // Green-50
            ".jpg" or ".jpeg" => new SolidColorBrush(Color.FromRgb(0xF5, 0xF3, 0xFF)), // Violet-50
            ".png" => new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0xF8)), // Pink-50
            ".gif" or ".bmp" or ".webp" or ".tiff" or ".heic" => new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB)), // Amber-50
            _ => new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)) // Gray-50
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Возвращает foreground/stroke SolidColorBrush на основе расширения файла.
/// </summary>
public class FileExtensionToForegroundBrushConverter : IValueConverter
{
    public static readonly FileExtensionToForegroundBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = (value?.ToString() ?? "").ToLower().Trim();
        if (string.IsNullOrEmpty(input)) return new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));

        if (input.Contains("/"))
        {
            if (input.Contains("wordprocessingml") || input.Contains("msword")) input = ".docx";
            else if (input.Contains("spreadsheetml") || input.Contains("ms-excel")) input = ".xlsx";
            else if (input.Contains("pdf")) input = ".pdf";
            else if (input.Contains("image/png")) input = ".png";
            else if (input.Contains("image/jpeg")) input = ".jpg";
        }

        var ext = Path.GetExtension(input);
        if (string.IsNullOrEmpty(ext)) ext = "." + input;

        return ext switch
        {
            ".doc" or ".docx" => new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), // Blue-600
            ".pdf" => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), // Red-600
            ".xls" or ".xlsx" or ".csv" => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)), // Green-600
            ".jpg" or ".jpeg" => new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), // Violet-600
            ".png" => new SolidColorBrush(Color.FromRgb(0xDB, 0x27, 0x77)), // Pink-600
            ".gif" or ".bmp" or ".webp" or ".tiff" or ".heic" => new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)), // Amber-600
            _ => new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)) // Gray-600
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long size)
        {
            if (size < 1024)
                return $"{size} Б";
            if (size < 1024 * 1024)
                return $"{size / 1024.0:F1} КБ";
            return $"{size / (1024.0 * 1024.0):F1} МБ";
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Возвращает короткую метку в верхнем регистре для расширения файла (например ".docx" -> "DOCX").
/// </summary>
public class FileExtensionToShortLabelConverter : IValueConverter
{
    public static readonly FileExtensionToShortLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var input = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(input)) return "";

        var ext = Path.GetExtension(input);
        if (string.IsNullOrEmpty(ext)) ext = input;

        return ext.TrimStart('.').ToUpperInvariant();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PluralTaskConverter : IValueConverter
{
    public static readonly PluralTaskConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int count) return " задач";
        var lastTwo = count % 100;
        var lastOne = count % 10;

        if (lastTwo >= 11 && lastTwo <= 19)
            return " задач";
        if (lastOne == 1)
            return " задача";
        if (lastOne >= 2 && lastOne <= 4)
            return " задачи";
        return " задач";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PluralStageConverter : IValueConverter
{
    public static readonly PluralStageConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int count) return " этапов";
        var lastTwo = count % 100;
        var lastOne = count % 10;

        if (lastTwo >= 11 && lastTwo <= 19)
            return " этапов";
        if (lastOne == 1)
            return " этап";
        if (lastOne >= 2 && lastOne <= 4)
            return " этапа";
        return " этапов";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BreakpointToDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double width = value is double d ? d : 0;
        double breakpoint = 900, small = 220, large = 320;
        if (parameter is string p)
        {
            var parts = p.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out breakpoint);
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out small);
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out large);
            }
        }
        return width <= breakpoint ? small : large;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
