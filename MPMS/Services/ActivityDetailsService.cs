using System.Globalization;
using MPMS.Models;

namespace MPMS.Services;

public static class ActivityDetailsService
{
    private const int MaxGroupedNamesToShow = 5;
    public const int MaxItemsInActivityBadge = 3;

    public static string GetTooltipTitle(LocalActivityLog log)
    {
        if (log.GroupCount > 1 && !string.IsNullOrWhiteSpace(log.ActionText))
            return log.ActionText;

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
        if (log.GroupCount > 1)
        {
            var groupedLines = ParseDetailLines(log.DetailsText);
            if (groupedLines.Count > 0)
                return groupedLines;

            return [log.ActionText];
        }

        if (log.ActionType is ActivityActionKind.MaterialAdded or ActivityActionKind.WorkTypeAdded
            && TryParseStageItemsAdded(log.ActionText, out _, out var items)
            && items.Count > 0)
            return items;

        var source = string.IsNullOrWhiteSpace(log.DetailsText)
            ? BuildGenericDetails(log.ActionText, log.EntityType, log.ActionType)
            : log.DetailsText;

        if (source.Contains("───"))
            return CompactLegacyGroupedDetails(source, log);

        var lines = ParseDetailLines(source);

        if (lines.Count == 0)
            lines.Add(log.ActionText);

        if (lines.Count == 1 && lines[0] == log.ActionText)
        {
            lines.Add($"Операция: {GetActionDisplay(log.ActionType)}");
            lines.Add($"Объект: {GetEntityDisplay(log.EntityType)}");
        }

        return lines;
    }

    public static string BuildGroupedDetailsText(IReadOnlyList<LocalActivityLog> activities)
    {
        if (activities.Count == 0)
            return string.Empty;
        if (activities.Count == 1)
            return activities[0].DetailsText ?? BuildGenericDetails(
                activities[0].ActionText, activities[0].EntityType, activities[0].ActionType);

        var first = activities[0];
        var entityType = first.EntityType ?? string.Empty;
        var actionType = first.ActionType ?? string.Empty;
        var count = activities.Count;

        var entityNames = activities
            .Select(a => ExtractEntityName(a.ActionText))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
        var uniqueNames = entityNames.Distinct(StringComparer.Ordinal).ToList();
        var isSingleEntity = uniqueNames.Count == 1;

        var lines = new List<string>();

        if (isSingleEntity && !string.IsNullOrEmpty(uniqueNames[0]))
        {
            lines.Add($"{GetEntityDisplay(entityType)} «{uniqueNames[0]}»");
            foreach (var activity in activities)
            {
                var change = ExtractMeaningfulDetail(activity, uniqueNames[0]);
                if (!string.IsNullOrWhiteSpace(change))
                    lines.Add(change);
            }
        }
        else if (entityType is "File" or "Image" or "Document")
        {
            var names = activities
                .Select(a => ExtractEntityName(a.ActionText))
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
            var header = actionType switch
            {
                ActivityActionKind.Deleted or ActivityActionKind.PermanentlyDeleted => "Удалённые файлы",
                _ => entityType switch
                {
                    "Image" => "Изображения",
                    "Document" => "Документы",
                    _ => "Файлы"
                }
            };
            AppendNameList(lines, names, header);
        }
        else if (entityType == "Message" && actionType == ActivityActionKind.Message)
        {
            foreach (var activity in activities.Take(MaxGroupedNamesToShow))
            {
                var preview = ExtractMeaningfulDetail(activity, ExtractEntityName(activity.ActionText));
                if (!string.IsNullOrWhiteSpace(preview))
                    lines.Add(preview);
            }

            if (activities.Count > MaxGroupedNamesToShow)
                lines.Add($"и ещё {activities.Count - MaxGroupedNamesToShow} сообщений");
        }
        else
        {
            var groupedByEntity = activities
                .GroupBy(a => ExtractEntityName(a.ActionText) ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            foreach (var group in groupedByEntity)
            {
                if (group.Count() == 1)
                {
                    var activity = group.First();
                    var name = group.Key;
                    var change = ExtractMeaningfulDetail(activity, name);
                    lines.Add(string.IsNullOrEmpty(name)
                        ? change ?? activity.ActionText
                        : $"«{name}»: {change ?? activity.ActionText}");
                    continue;
                }

                if (!string.IsNullOrEmpty(group.Key))
                    lines.Add($"{GetEntityDisplay(entityType)} «{group.Key}»");

                foreach (var activity in group)
                {
                    var change = ExtractMeaningfulDetail(activity, group.Key);
                    if (!string.IsNullOrWhiteSpace(change))
                        lines.Add(change);
                }
            }
        }

        var actionDisplay = GetActionDisplay(actionType);
        var timesSuffix = count > 1 ? $" ({count} {PluralizeTimes(count)})" : string.Empty;
        lines.Add($"Операция: {actionDisplay}{timesSuffix}");

        return string.Join(Environment.NewLine, lines);
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

    public static string? BuildTaskUpdateDetails(LocalTask before, UpdateTaskRequest next, string? assignedName, bool includeStatus, bool skipAssignee = false)
    {
        var changes = new List<string>();
        AddChange(changes, "Название", before.Name, next.Name);
        AddChange(changes, "Описание", before.Description, next.Description);
        if (!skipAssignee)
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

    private static string? ExtractEntityName(string actionText) => ExtractQuotedValue(actionText);

    private static List<string> ParseDetailLines(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        return source
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Select(x => x.StartsWith("───") ? x : x.TrimStart('-', '•').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static IReadOnlyList<string> CompactLegacyGroupedDetails(string source, LocalActivityLog log)
    {
        var blocks = source
            .Split(["\r\n───\r\n", "\n───\n", "\r\n───\n", "\n───\r\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseDetailLines)
            .Where(b => b.Count > 0)
            .ToList();

        if (blocks.Count <= 1)
            return ParseDetailLines(source);

        var syntheticActivities = blocks.Select(block =>
        {
            var actionLine = block.FirstOrDefault(l =>
                l.StartsWith("Что сделано:", StringComparison.OrdinalIgnoreCase)) ?? block[0];
            var actionText = actionLine.StartsWith("Что сделано:", StringComparison.OrdinalIgnoreCase)
                ? actionLine["Что сделано:".Length..].Trim()
                : actionLine;

            return new LocalActivityLog
            {
                ActionText = actionText,
                DetailsText = string.Join(Environment.NewLine, block),
                EntityType = log.EntityType,
                ActionType = log.ActionType
            };
        }).ToList();

        return ParseDetailLines(BuildGroupedDetailsText(syntheticActivities));
    }

    private static string ExtractMeaningfulDetail(LocalActivityLog activity, string? entityName)
    {
        if (!string.IsNullOrWhiteSpace(activity.DetailsText))
        {
            var detailLines = ParseDetailLines(activity.DetailsText);
            var meaningful = detailLines
                .Where(l => l != "───")
                .Select(StripBoilerplatePrefix)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            var changes = meaningful.Where(l => l.Contains('→')).ToList();
            if (changes.Count > 0)
                return string.Join("; ", changes);

            if (meaningful.Count == 1)
            {
                var compact = ExtractChangeAfterEntity(meaningful[0], entityName);
                if (!string.IsNullOrWhiteSpace(compact))
                    return compact;
            }
        }

        return ExtractChangeAfterEntity(activity.ActionText, entityName) ?? activity.ActionText;
    }

    private static string StripBoilerplatePrefix(string line)
    {
        foreach (var prefix in new[] { "Что сделано:", "Операция:", "Раздел:", "Объект:" })
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].Trim();
        }

        return line;
    }

    private static string? ExtractChangeAfterEntity(string actionText, string? entityName)
    {
        if (string.IsNullOrWhiteSpace(actionText))
            return null;

        if (string.IsNullOrWhiteSpace(entityName))
            return actionText;

        var marker = $"«{entityName}»";
        var idx = actionText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return actionText;

        var before = actionText[..idx].TrimEnd();
        var after = actionText[(idx + marker.Length)..].TrimStart(' ', ',', ':', '—', '-');

        if (!string.IsNullOrWhiteSpace(after))
            return after;

        if (!string.IsNullOrWhiteSpace(before))
        {
            before = before
                .Replace("оборудования", "", StringComparison.OrdinalIgnoreCase)
                .Replace("материала", "", StringComparison.OrdinalIgnoreCase)
                .Replace("  ", " ")
                .TrimEnd();
            return before;
        }

        return actionText;
    }

    private static void AppendNameList(List<string> lines, List<string> names, string header)
    {
        if (names.Count == 0)
            return;

        lines.Add($"{header}:");
        foreach (var name in names.Take(MaxGroupedNamesToShow))
            lines.Add(name);

        if (names.Count > MaxGroupedNamesToShow)
            lines.Add($"и ещё {names.Count - MaxGroupedNamesToShow}");
    }

    private static string PluralizeTimes(int count)
    {
        var lastTwo = count % 100;
        var lastOne = count % 10;
        if (lastTwo is >= 11 and <= 19)
            return "раз";
        return lastOne switch
        {
            1 => "раз",
            >= 2 and <= 4 => "раза",
            _ => "раз"
        };
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

    public static string GetActivityDisplayText(LocalActivityLog log)
    {
        if (TryParseStageItemsAdded(log.ActionText, out var prefix, out var items)
            && items.Count > MaxItemsInActivityBadge)
            return prefix + FormatItemPreviewList(items);

        return log.ActionText;
    }

    public static bool TryParseStageItemsAdded(
        string actionText,
        out string itemsPrefix,
        out List<string> items)
    {
        itemsPrefix = actionText;
        items = [];

        ReadOnlySpan<string> markers = ["добавлены материалы: ", "добавлены виды работ: "];
        foreach (var marker in markers)
        {
            var idx = actionText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            itemsPrefix = actionText[..(idx + marker.Length)];
            var listPart = actionText[(idx + marker.Length)..].Trim();
            if (listPart.EndsWith(", ...", StringComparison.Ordinal))
                listPart = listPart[..^5].TrimEnd();

            items = listPart
                .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x != "...")
                .ToList();
            return true;
        }

        return false;
    }

    public static string FormatItemPreviewList(IReadOnlyList<string> names, int maxItems = MaxItemsInActivityBadge)
    {
        if (names.Count == 0) return string.Empty;
        if (names.Count <= maxItems)
            return string.Join(", ", names);
        return string.Join(", ", names.Take(maxItems)) + ", ...";
    }

    /// <summary>Подпись материала/позиции в ленте активности: количество — только если добавлено не 1.</summary>
    public static string FormatAddedStageItemLabel(string name, decimal addedQty, string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        if (addedQty <= 1m)
            return name;

        var qtyText = MaterialUnits.IsIntegerUnit(unit) && addedQty == Math.Floor(addedQty)
            ? ((int)addedQty).ToString(CultureInfo.InvariantCulture)
            : addedQty.ToString("0.###", CultureInfo.InvariantCulture);
        var unitSuffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        return $"{name} × {qtyText}{unitSuffix}";
    }
}
