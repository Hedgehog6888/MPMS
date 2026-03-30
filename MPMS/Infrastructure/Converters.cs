using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

/// <summary>Converts to Visibility: Visible when value equals parameter (string comparison).</summary>
public class EqualityToVisibilityConverter : IValueConverter
{
    public static readonly EqualityToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
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

/// <summary>Converts avatar file path to ImageSource for display. Returns null when path is invalid or file missing.</summary>
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
/// Converts avatar byte array (PNG stored in DB) to ImageSource for display.
/// Returns null if data is null or empty — caller shows initials circle fallback.
/// </summary>
public class AvatarBytesToImageSourceConverter : IValueConverter
{
    public static readonly AvatarBytesToImageSourceConverter Instance = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bytes = value as byte[];
        return MPMS.Services.AvatarHelper.BytesToBitmapImage(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts bool IsBlocked to a localized status string: "Активен" / "Заблокирован".
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
/// Converts bool IsBlocked to a SolidColorBrush:
/// true → red (#EF4444), false → green (#22C55E).
/// </summary>
public class BlockedToStatusBrushConverter : IValueConverter
{
    public static readonly BlockedToStatusBrushConverter Instance = new();

    private static readonly SolidColorBrush ActiveBrush  = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush BlockedBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? BlockedBrush : ActiveBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts AdminActionKind string to localized Russian label for history log.
/// </summary>
public class ActionKindToLabelConverter : IValueConverter
{
    public static readonly ActionKindToLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            MPMS.Models.ActivityActionKind.Created            => "Создан",
            MPMS.Models.ActivityActionKind.Updated            => "Изменён",
            MPMS.Models.ActivityActionKind.Deleted            => "Удалён",
            MPMS.Models.ActivityActionKind.MarkedForDeletion  => "Помечен на удаление",
            MPMS.Models.ActivityActionKind.UnmarkedForDeletion => "Снята пометка",
            MPMS.Models.ActivityActionKind.Message            => "Сообщение",
            MPMS.Models.ActivityActionKind.Login              => "Вход",
            MPMS.Models.ActivityActionKind.Logout             => "Выход",
            MPMS.Models.ActivityActionKind.PasswordChanged    => "Смена пароля",
            MPMS.Models.ActivityActionKind.AvatarChanged      => "Смена аватара",
            MPMS.Models.ActivityActionKind.UserCreated        => "Создан пользователь",
            MPMS.Models.ActivityActionKind.UserEdited         => "Изменён пользователь",
            MPMS.Models.ActivityActionKind.UserBlocked        => "Заблокирован",
            MPMS.Models.ActivityActionKind.UserUnblocked      => "Разблокирован",
            MPMS.Models.ActivityActionKind.UserDeleted        => "Удалён пользователь",
            MPMS.Models.ActivityActionKind.Restored           => "Восстановлен",
            MPMS.Models.ActivityActionKind.PermanentlyDeleted => "Удалён навсегда",
            _                                                  => value?.ToString() ?? "—"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts AdminActionKind string to a SolidColorBrush for history log badges.
/// </summary>
public class ActionKindToBrushConverter : IValueConverter
{
    public static readonly ActionKindToBrushConverter Instance = new();

    private static readonly SolidColorBrush BlueBrush   = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
    private static readonly SolidColorBrush GreenBrush  = new(Color.FromRgb(0x16, 0xA3, 0x4A));
    private static readonly SolidColorBrush RedBrush    = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(0xF9, 0x73, 0x16));
    private static readonly SolidColorBrush PurpleBrush = new(Color.FromRgb(0x9C, 0x6A, 0xFE));
    private static readonly SolidColorBrush GrayBrush   = new(Color.FromRgb(0x6B, 0x77, 0x8C));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            MPMS.Models.ActivityActionKind.Created or
            MPMS.Models.ActivityActionKind.UserCreated        => BlueBrush,

            MPMS.Models.ActivityActionKind.Login or
            MPMS.Models.ActivityActionKind.UnmarkedForDeletion or
            MPMS.Models.ActivityActionKind.Restored or
            MPMS.Models.ActivityActionKind.UserUnblocked      => GreenBrush,

            MPMS.Models.ActivityActionKind.Deleted or
            MPMS.Models.ActivityActionKind.PermanentlyDeleted or
            MPMS.Models.ActivityActionKind.UserDeleted or
            MPMS.Models.ActivityActionKind.UserBlocked        => RedBrush,

            MPMS.Models.ActivityActionKind.MarkedForDeletion or
            MPMS.Models.ActivityActionKind.PasswordChanged or
            MPMS.Models.ActivityActionKind.AvatarChanged or
            MPMS.Models.ActivityActionKind.Updated or
            MPMS.Models.ActivityActionKind.UserEdited         => OrangeBrush,

            MPMS.Models.ActivityActionKind.Message            => PurpleBrush,

            _                                                  => GrayBrush
        };

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
    private static readonly System.Globalization.CultureInfo RuCulture =
        System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    public static readonly DateOnlyToStringConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateOnly d) return "—";
        string fmt = parameter as string ?? "short";
        return fmt switch
        {
            "long"    => d.ToString("d MMMM yyyy", RuCulture),
            "dayname" => d.DayOfWeek switch
            {
                DayOfWeek.Monday    => "понедельник",
                DayOfWeek.Tuesday   => "вторник",
                DayOfWeek.Wednesday => "среда",
                DayOfWeek.Thursday  => "четверг",
                DayOfWeek.Friday    => "пятница",
                DayOfWeek.Saturday  => "суббота",
                DayOfWeek.Sunday    => "воскресенье",
                _                   => d.DayOfWeek.ToString()
            },
            _ => d.ToString("dd.MM.yyyy")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts int to bool: true if value > 0.</summary>
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

/// <summary>Converts int (stage count) to localized string like "3 этапа".</summary>
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

/// <summary>Converts a DateTime to a relative or formatted time string.</summary>
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

/// <summary>
/// Converts a required role string to Visibility based on current user's role.
/// Parameter: comma-separated list of roles that should see Visible (e.g., "Admin,Administrator").
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

/// <summary>Converts progress percent (int) to Brush: red &lt;30%, orange 30–59%, blue 60–99%, green 100%.</summary>
public class ProgressPercentToBrushConverter : IValueConverter
{
    public static readonly ProgressPercentToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var pct = value is int i ? i : 0;
        return pct >= 100
            ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
            : pct >= 60
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF))
                : pct >= 30
                    ? new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps EntityType string to an accent SolidColorBrush for activity log items.</summary>
public class EntityTypeToAccentBrushConverter : IValueConverter
{
    public static readonly EntityTypeToAccentBrushConverter Instance = new();

    private static readonly SolidColorBrush ProjectBrush  = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
    private static readonly SolidColorBrush TaskBrush     = new(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly SolidColorBrush StageBrush    = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush MessageBrush  = new(Color.FromRgb(0x9C, 0x6A, 0xFE));
    private static readonly SolidColorBrush DefaultBrush  = new(Color.FromRgb(0x6B, 0x77, 0x8C));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            "Project"  => ProjectBrush,
            "Task"     => TaskBrush,
            "Stage"    => StageBrush,
            "Message"  => MessageBrush,
            _          => DefaultBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps LocalActivityLog to accent Brush — prefers ActionType (Deleted, MarkedForDeletion, etc.) over EntityType.</summary>
public class ActivityLogToAccentBrushConverter : IValueConverter
{
    public static readonly ActivityLogToAccentBrushConverter Instance = new();

    private static readonly SolidColorBrush DeletedBrush         = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush MarkedForDeletionBrush = new(Color.FromRgb(0xF9, 0x73, 0x16));
    private static readonly SolidColorBrush UnmarkedBrush       = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush CreatedBrush        = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
    private static readonly SolidColorBrush MessageBrush        = new(Color.FromRgb(0x9C, 0x6A, 0xFE));
    private static readonly SolidColorBrush ProjectBrush        = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
    private static readonly SolidColorBrush TaskBrush           = new(Color.FromRgb(0xEA, 0xB3, 0x08));
    private static readonly SolidColorBrush StageBrush          = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush DefaultBrush        = new(Color.FromRgb(0x6B, 0x77, 0x8C));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MPMS.Models.LocalActivityLog log)
            return DefaultBrush;

        var actionType = log.ActionType;
        if (!string.IsNullOrEmpty(actionType))
        {
            return actionType switch
            {
                MPMS.Models.ActivityActionKind.Deleted           => DeletedBrush,
                MPMS.Models.ActivityActionKind.MarkedForDeletion => MarkedForDeletionBrush,
                MPMS.Models.ActivityActionKind.UnmarkedForDeletion => UnmarkedBrush,
                MPMS.Models.ActivityActionKind.Created          => CreatedBrush,
                MPMS.Models.ActivityActionKind.Message          => MessageBrush,
                _ => EntityToBrush(log.EntityType)
            };
        }
        return EntityToBrush(log.EntityType);
    }

    private static SolidColorBrush EntityToBrush(string entityType) => entityType switch
    {
        "Project"  => ProjectBrush,
        "Task"     => TaskBrush,
        "Stage"    => StageBrush,
        "Message"  => MessageBrush,
        _          => DefaultBrush
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps EntityType string to a localized Russian label for activity log badges.</summary>
public class EntityTypeToBadgeLabelConverter : IValueConverter
{
    public static readonly EntityTypeToBadgeLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "") switch
        {
            "Project"  => "Проект",
            "Task"     => "Задача",
            "Stage"    => "Этап",
            "Message"  => "Сообщение",
            _          => "—"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps ActorRole to localized Russian label for activity log role badge.</summary>
public class ActorRoleToLabelConverter : IValueConverter
{
    public static readonly ActorRoleToLabelConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => NormalizeRoleKey(value?.ToString()) switch
        {
            "admin"    => "Админ",
            "manager"  => "Менеджер",
            "foreman"  => "Прораб",
            "worker"   => "Работник",
            _          => ""
        };

    /// <summary>English keys, Russian titles from messages (RoleToRussian), short forms.</summary>
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

/// <summary>Maps ActorRole to light pastel Brush for role badge background (distinct from entity badges).</summary>
public class ActorRoleToBrushConverter : IValueConverter
{
    public static readonly ActorRoleToBrushConverter Instance = new();

    private static readonly SolidColorBrush AdminBrush    = new(Color.FromRgb(0xFE, 0xE2, 0xE2));
    private static readonly SolidColorBrush ManagerBrush  = new(Color.FromRgb(0xDB, 0xE8, 0xFE));
    private static readonly SolidColorBrush ForemanBrush  = new(Color.FromRgb(0xD1, 0xFA, 0xE5));
    private static readonly SolidColorBrush WorkerBrush   = new(Color.FromRgb(0xED, 0xE9, 0xFE));
    private static readonly SolidColorBrush DefaultBrush  = new(Color.FromRgb(0xF1, 0xF3, 0xF5));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ActorRoleToLabelConverter.NormalizeRoleKey(value?.ToString()) switch
        {
            "admin"    => AdminBrush,
            "manager"  => ManagerBrush,
            "foreman"  => ForemanBrush,
            "worker"   => WorkerBrush,
            _          => DefaultBrush
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps ActorRole to foreground Brush for role badge text (dark accent).</summary>
public class ActorRoleToForegroundBrushConverter : IValueConverter
{
    public static readonly ActorRoleToForegroundBrushConverter Instance = new();

    private static readonly SolidColorBrush AdminBrush    = new(Color.FromRgb(0x99, 0x1B, 0x1B));
    private static readonly SolidColorBrush ManagerBrush  = new(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly SolidColorBrush ForemanBrush  = new(Color.FromRgb(0x16, 0x65, 0x34));
    private static readonly SolidColorBrush WorkerBrush   = new(Color.FromRgb(0x6D, 0x28, 0xD9));
    private static readonly SolidColorBrush DefaultBrush  = new(Color.FromRgb(0x4B, 0x55, 0x63));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ActorRoleToLabelConverter.NormalizeRoleKey(value?.ToString()) switch
        {
            "admin"    => AdminBrush,
            "manager"  => ManagerBrush,
            "foreman"  => ForemanBrush,
            "worker"   => WorkerBrush,
            _          => DefaultBrush
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

/// <summary>Null/empty check: Visible when value is NOT null/empty, Collapsed otherwise.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; init; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value switch
        {
            null             => false,
            byte[] b         => b.Length > 0,
            string s         => !string.IsNullOrEmpty(s),
            _                => true
        };
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps TaskStatus to a pale/tinted background Brush (light version of the status colour).</summary>
public class TaskStatusToPaleBrushConverter : IValueConverter
{
    public static readonly TaskStatusToPaleBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskStatus s ? s switch
        {
            TaskStatus.Planned    => new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5)),  // pale gray
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),  // pale blue
            TaskStatus.Paused     => new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5)),  // same gray as planned
            TaskStatus.Completed  => new SolidColorBrush(Color.FromRgb(0xE3, 0xFC, 0xEF)),  // pale green
            _                     => new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5))
        } : new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps TaskStatus to the dark foreground colour matching the pale background badge.</summary>
public class TaskStatusToForegroundBrushConverter : IValueConverter
{
    public static readonly TaskStatusToForegroundBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskStatus s ? s switch
        {
            TaskStatus.Planned    => new SolidColorBrush(Color.FromRgb(0x42, 0x52, 0x6E)),  // dark gray
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0x00, 0x52, 0xCC)),  // dark blue
            TaskStatus.Paused     => new SolidColorBrush(Color.FromRgb(0x42, 0x52, 0x6E)),  // dark gray
            TaskStatus.Completed  => new SolidColorBrush(Color.FromRgb(0x00, 0x66, 0x44)),  // dark green
            _                     => new SolidColorBrush(Color.FromRgb(0x42, 0x52, 0x6E))
        } : new SolidColorBrush(Color.FromRgb(0x42, 0x52, 0x6E));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a 0–1 double fraction to a Star GridLength for proportional Gantt bar columns.</summary>
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

/// <summary>Converts initials string to a deterministic SolidColorBrush accent color.</summary>
public class InitialsToBrushConverter : IValueConverter
{
    private static readonly string[] Palette =
    {
        "#1B6EC2", "#C0392B", "#27AE60", "#8E44AD",
        "#E67E22", "#16A085", "#2980B9", "#D35400"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(s)) return new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2));
        int hash = 0;
        foreach (var c in s) hash = hash * 31 + c;
        var hex = Palette[Math.Abs(hash) % Palette.Length];
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch { return new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)); }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
