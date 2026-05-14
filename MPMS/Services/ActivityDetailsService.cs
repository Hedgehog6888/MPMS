using System.Globalization;
using MPMS.Models;

namespace MPMS.Services;

public static class ActivityDetailsService
{
    public static string GetTooltipTitle(LocalActivityLog log)
    {
        var entity = GetEntityDisplay(log.EntityType);
        var action = GetActionDisplay(log.ActionType);
        return $"{entity}: {action}";
    }

    public static string GetActionDisplay(string? actionType) => actionType switch
    {
        ActivityActionKind.Created => "создание",
        ActivityActionKind.Updated => "изменение",
        ActivityActionKind.Deleted => "удаление / архив",
        ActivityActionKind.MarkedForDeletion => "пометка к удалению",
        ActivityActionKind.UnmarkedForDeletion => "снятие пометки",
        ActivityActionKind.Restored => "восстановление",
        ActivityActionKind.PermanentlyDeleted => "удаление навсегда",
        ActivityActionKind.Message => "сообщение",
        ActivityActionKind.Login => "вход",
        ActivityActionKind.Logout => "выход",
        ActivityActionKind.PasswordChanged => "смена пароля",
        ActivityActionKind.AvatarChanged => "смена аватара",
        ActivityActionKind.UserCreated => "создание пользователя",
        ActivityActionKind.UserEdited => "изменение пользователя",
        ActivityActionKind.UserDeleted => "удаление пользователя",
        ActivityActionKind.UserBlocked => "блокировка пользователя",
        ActivityActionKind.UserUnblocked => "разблокировка пользователя",
        ActivityActionKind.StatusChanged => "изменение статуса",
        ActivityActionKind.TaskStatusChanged => "изменение статуса задачи",
        ActivityActionKind.StageStatusChanged => "изменение статуса этапа",
        ActivityActionKind.MemberAdded => "добавление участника",
        ActivityActionKind.MemberRemoved => "удаление участника",
        ActivityActionKind.MaterialAdded => "добавление материала",
        ActivityActionKind.MaterialRemoved => "удаление материала",
        ActivityActionKind.WorkTypeAdded => "добавление вида работ",
        ActivityActionKind.WorkTypeRemoved => "удаление вида работ",
        _ => "действие"
    };

    public static string GetEntityDisplay(string? entityType) => entityType switch
    {
        "Project" => "Проект",
        "Task" => "Задача",
        "Stage" => "Этап",
        "File" => "Файл",
        "Image" => "Изображение",
        "Document" => "Документ",
        "Message" => "Обсуждение",
        "Material" => "Материал",
        "Equipment" => "Оборудование",
        "User" => "Пользователь",
        _ => "Объект"
    };

    public static IReadOnlyList<string> GetTooltipDetailLines(LocalActivityLog log)
    {
        var source = string.IsNullOrWhiteSpace(log.DetailsText)
            ? BuildGenericDetails(log.ActionText, log.EntityType, log.ActionType)
            : log.DetailsText;
        var lines = source
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Select(x => x.StartsWith("───") ? x : x.TrimStart('-', '•').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (lines.Count == 0)
            lines.Add(log.ActionText);

        if (lines.Count == 1 && lines[0] == log.ActionText)
        {
            lines.Add($"Операция: {GetActionDisplay(log.ActionType)}");
            lines.Add($"Объект: {GetEntityDisplay(log.EntityType)}");
        }

        return lines;
    }

    public static string BuildGenericDetails(string actionText, string entityType, string? actionType)
    {
        var lines = new List<string>
        {
            $"Что сделано: {actionText}",
            $"Операция: {GetActionDisplay(actionType)}",
            $"Раздел: {GetEntityDisplay(entityType)}"
        };

        var entityName = ExtractQuotedValue(actionText);
        if (!string.IsNullOrWhiteSpace(entityName))
            lines.Add($"Объект: {entityName}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string? BuildProjectUpdateDetails(LocalProject before, UpdateProjectRequest next, string managerName)
    {
        var changes = new List<string>();
        AddChange(changes, "Название", before.Name, next.Name);
        AddChange(changes, "Описание", before.Description, next.Description);
        AddChange(changes, "Клиент", before.Client, next.Client);
        AddChange(changes, "Адрес", before.Address, next.Address);
        AddChange(changes, "Дата начала", FormatDate(before.StartDate), FormatDate(next.StartDate));
        AddChange(changes, "Дата окончания", FormatDate(before.EndDate), FormatDate(next.EndDate));
        AddChange(changes, "Руководитель", before.ManagerName, managerName);
        AddChange(changes, "Статус удаления", FormatBool(before.IsMarkedForDeletion), FormatBool(next.IsMarkedForDeletion));
        AddChange(changes, "Архив", FormatBool(before.IsArchived), FormatBool(next.IsArchived));
        AddChange(changes, "Закрыт", FormatBool(before.IsClosed), FormatBool(next.IsClosed));
        AddChange(changes, "Причина закрытия", before.ClosureReason, next.ClosureReason);
        return BuildDetails("Проект", before.Name, changes);
    }

    public static string? BuildTaskUpdateDetails(LocalTask before, UpdateTaskRequest next, string? assignedName, bool includeStatus)
    {
        var changes = new List<string>();
        AddChange(changes, "Название", before.Name, next.Name);
        AddChange(changes, "Описание", before.Description, next.Description);
        AddChange(changes, "Исполнитель", before.AssignedUserName, assignedName);
        AddChange(changes, "Приоритет", FormatPriority(before.Priority), FormatPriority(next.Priority));
        AddChange(changes, "Срок", FormatDate(before.DueDate), FormatDate(next.DueDate));
        if (includeStatus)
            AddChange(changes, "Статус", FormatTaskStatus(before.Status), FormatTaskStatus(next.Status));
        AddChange(changes, "Статус удаления", FormatBool(before.IsMarkedForDeletion), FormatBool(next.IsMarkedForDeletion));
        AddChange(changes, "Архив", FormatBool(before.IsArchived), FormatBool(next.IsArchived));
        return BuildDetails("Задача", before.Name, changes);
    }

    public static string? BuildStageUpdateDetails(LocalTaskStage before, UpdateStageRequest next, string? assignedName, bool servicesWereSubmitted)
    {
        var changes = new List<string>();
        AddChange(changes, "Название", before.Name, next.Name);
        AddChange(changes, "Описание", before.Description, next.Description);
        AddChange(changes, "Исполнитель", before.AssignedUserName, assignedName);
        AddChange(changes, "Статус", FormatStageStatus(before.Status), FormatStageStatus(next.Status));
        AddChange(changes, "Срок", FormatDate(before.DueDate), FormatDate(next.DueDate));
        AddChange(changes, "Объём работ", FormatDecimal(before.WorkQuantity), FormatDecimal(next.WorkQuantity));
        AddChange(changes, "Цена за единицу", FormatMoney(before.WorkPricePerUnit), FormatMoney(next.WorkPricePerUnit));
        AddChange(changes, "Статус удаления", FormatBool(before.IsMarkedForDeletion), FormatBool(next.IsMarkedForDeletion));
        AddChange(changes, "Архив", FormatBool(before.IsArchived), FormatBool(next.IsArchived));
        if (servicesWereSubmitted)
            changes.Add("Состав работ: обновлён");
        return BuildDetails("Этап", before.Name, changes);
    }

    public static string? BuildSingleDetail(string header, params string?[] lines)
    {
        var parts = lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine, new[] { header }.Concat(parts));
    }

    private static string? BuildDetails(string entityLabel, string entityName, List<string> changes)
    {
        if (changes.Count == 0)
            return $"{entityLabel} «{entityName}»: изменений в основных полях не обнаружено";
        return string.Join(Environment.NewLine, new[] { $"{entityLabel} «{entityName}»: изменено" }.Concat(changes.Select(c => $"- {c}")));
    }

    private static void AddChange(List<string> changes, string label, string? oldValue, string? newValue)
    {
        oldValue = Normalize(oldValue);
        newValue = Normalize(newValue);
        if (oldValue == newValue) return;
        changes.Add($"{label}: {Display(oldValue)} → {Display(newValue)}");
    }

    private static string? Normalize(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized;
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static string? ExtractQuotedValue(string value)
    {
        var start = value.IndexOf('«');
        var end = value.IndexOf('»', start + 1);
        return start >= 0 && end > start ? value.Substring(start + 1, end - start - 1) : null;
    }
    private static string? FormatDate(DateOnly? value) => value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string FormatBool(bool value) => value ? "Да" : "Нет";
    private static string FormatDecimal(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string FormatMoney(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatTaskStatus(Models.TaskStatus value) => value switch
    {
        Models.TaskStatus.Planned => "Запланирована",
        Models.TaskStatus.InProgress => "Выполняется",
        Models.TaskStatus.Paused => "Приостановлена",
        Models.TaskStatus.Completed => "Завершена",
        _ => value.ToString()
    };

    private static string FormatStageStatus(StageStatus value) => value switch
    {
        StageStatus.Planned => "Запланирован",
        StageStatus.InProgress => "Выполняется",
        StageStatus.Completed => "Завершён",
        _ => value.ToString()
    };

    private static string FormatPriority(TaskPriority value) => value switch
    {
        TaskPriority.Low => "Низкий",
        TaskPriority.Medium => "Средний",
        TaskPriority.High => "Высокий",
        TaskPriority.Critical => "Критический",
        _ => value.ToString()
    };
}
