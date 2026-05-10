using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using System.Text;

namespace MPMS.Services;

/// <summary>Filters activity log by role: Admin=all, Manager=all except admin, Foreman=self+collaborators, Worker=self+foremen on shared tasks/stages.</summary>
public static class ActivityFilterService
{
    /// <summary>Action types shown only in admin History/Activity tabs (excluded from recent activity).</summary>
    private static readonly HashSet<string> AdminOnlyEventKinds = new()
    {
        ActivityActionKind.Login, ActivityActionKind.Logout,
        ActivityActionKind.PasswordChanged, ActivityActionKind.AvatarChanged,
        ActivityActionKind.UserCreated, ActivityActionKind.UserEdited, ActivityActionKind.UserDeleted,
        ActivityActionKind.UserBlocked, ActivityActionKind.UserUnblocked
    };

    /// <summary>Time window for grouping messages (seconds).</summary>
    private const int MessageGroupingWindowSeconds = 60;

    /// <summary>Worker filter data: task/stage IDs and collaborator user IDs for context-based filtering.</summary>
    private class WorkerFilterData
    {
        public HashSet<Guid> TaskIds { get; set; } = new();
        public HashSet<Guid> StageIds { get; set; } = new();
        // All user IDs who work on same tasks/stages (including foremen)
        public HashSet<Guid> CollaboratorIds { get; set; } = new();
        // File IDs linked to worker's tasks/stages
        public HashSet<Guid> RelatedFileIds { get; set; } = new();
        // Message IDs linked to worker's tasks/projects
        public HashSet<Guid> RelatedMessageIds { get; set; } = new();
    }

    public static async Task<List<LocalActivityLog>> GetFilteredActivitiesAsync(
        LocalDbContext db, IAuthService auth, int take = 10, bool excludeAuthEvents = true, CancellationToken ct = default)
    {
        var userRole = auth.UserRole ?? "";
        var currentUserId = auth.UserId;

        IQueryable<LocalActivityLog> query = db.ActivityLogs.OrderByDescending(a => a.CreatedAt);
        if (excludeAuthEvents)
            query = query.Where(a => a.ActionType == null || !AdminOnlyEventKinds.Contains(a.ActionType));

        // Admin: no post-filter — a single query is enough
        if (IsAdminRole(userRole))
        {
            var adminList = await query.Take(take).ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            await AttachAvatarsAsync(db, adminList, ct);
            return GroupActivities(adminList);
        }

        // Other roles: filtering can drop most recent rows (e.g. many admin actions, then login as manager).
        // Scan chronologically in batches until we have `take` visible entries or hit a scan cap.
        HashSet<Guid>? managerVisibleIds = null;
        if (IsManagerRole(userRole) && currentUserId.HasValue)
        {
            managerVisibleIds = await GetManagerVisibleUserIdsAsync(db, currentUserId.Value, ct);
            ct.ThrowIfCancellationRequested();
        }

        HashSet<Guid>? foremanVisibleIds = null;
        if (IsForemanRole(userRole) && currentUserId.HasValue)
        {
            foremanVisibleIds = await GetForemanVisibleUserIdsAsync(db, currentUserId.Value, ct);
            ct.ThrowIfCancellationRequested();
        }

        WorkerFilterData? workerFilterData = null;
        if (IsWorkerRole(userRole) && currentUserId.HasValue)
        {
            workerFilterData = await GetWorkerFilterDataAsync(db, currentUserId.Value, ct);
            ct.ThrowIfCancellationRequested();
        }

        const int batchSize = 150;
        var maxScan = Math.Min(20_000, Math.Max(2_000, take * 100));
        var result = new List<LocalActivityLog>(Math.Min(take, 32));
        var skip = 0;

        while (result.Count < take && skip < maxScan)
        {
            var batch = await query.Skip(skip).Take(batchSize).ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (batch.Count == 0)
                break;
            skip += batch.Count;

            foreach (var a in batch)
            {
                if (result.Count >= take)
                    break;
                if (PassesRoleFilter(a, userRole, currentUserId, managerVisibleIds, foremanVisibleIds, workerFilterData))
                    result.Add(a);
            }
        }

        await AttachAvatarsAsync(db, result, ct);
        return GroupActivities(result);
    }

    /// <summary>Groups all activities by user + action type within a short time window (60 seconds).</summary>
    private static List<LocalActivityLog> GroupActivities(List<LocalActivityLog> activities)
    {
        if (activities.Count == 0) return activities;

        var grouped = new List<LocalActivityLog>();
        var actionGroups = new Dictionary<string, List<LocalActivityLog>>();

        // Group all activities by user + action type + entity type
        foreach (var activity in activities)
        {
            var userId = activity.UserId ?? Guid.Empty;
            var actionType = activity.ActionType ?? "Unknown";
            var entityType = activity.EntityType ?? "Unknown";
            var key = $"{userId}_{actionType}_{entityType}";
            if (!actionGroups.ContainsKey(key))
                actionGroups[key] = new List<LocalActivityLog>();
            actionGroups[key].Add(activity);
        }

        // Process each action group
        foreach (var group in actionGroups.Values)
        {
            if (group.Count == 1)
            {
                grouped.Add(group[0]);
            }
            else
            {
                var sorted = group.OrderBy(a => a.CreatedAt).ToList();
                var currentGroup = new List<LocalActivityLog> { sorted[0] };

                for (int i = 1; i < sorted.Count; i++)
                {
                    var prev = sorted[i - 1];
                    var current = sorted[i];
                    var timeDiff = (current.CreatedAt - prev.CreatedAt).TotalSeconds;

                    if (timeDiff <= MessageGroupingWindowSeconds)
                    {
                        currentGroup.Add(current);
                    }
                    else
                    {
                        if (currentGroup.Count == 1)
                        {
                            grouped.Add(currentGroup[0]);
                        }
                        else
                        {
                            grouped.Add(CreateGroupedEntry(currentGroup));
                        }
                        currentGroup = new List<LocalActivityLog> { current };
                    }
                }

                if (currentGroup.Count == 1)
                {
                    grouped.Add(currentGroup[0]);
                }
                else
                {
                    grouped.Add(CreateGroupedEntry(currentGroup));
                }
            }
        }

        return grouped.OrderByDescending(a => a.CreatedAt).ToList();
    }

    /// <summary>Creates a single grouped activity entry from multiple activities.</summary>
    private static LocalActivityLog CreateGroupedEntry(List<LocalActivityLog> activities)
    {
        var first = activities[0];
        var last = activities[activities.Count - 1];

        var actionText = GenerateGroupedActionText(activities);

        return new LocalActivityLog
        {
            Id = first.Id,
            UserId = first.UserId,
            ActorRole = first.ActorRole,
            UserName = first.UserName,
            UserInitials = first.UserInitials,
            UserColor = first.UserColor,
            ActionType = first.ActionType,
            ActionText = actionText,
            DetailsText = string.Join(Environment.NewLine + "───" + Environment.NewLine,
                activities.Select(a => a.ActivityTooltipText).Where(x => !string.IsNullOrWhiteSpace(x))),
            EntityType = first.EntityType,
            EntityId = first.EntityId,
            CreatedAt = last.CreatedAt,
            GroupCount = activities.Count,
            AvatarData = first.AvatarData,
            AvatarPath = first.AvatarPath
        };
    }

    /// <summary>Generates a descriptive text for grouped activities.</summary>
    private static string GenerateGroupedActionText(List<LocalActivityLog> activities)
    {
        if (activities.Count == 0) return string.Empty;

        var first = activities[0];
        var actionType = first.ActionType;
        var entityType = first.EntityType;
        var count = activities.Count;

        // Extract context (task/project name) from first action
        var context = ExtractContextFromActionText(first.ActionText);

        // Extract entity names for enumeration
        var entityNames = activities
            .Select(a => ExtractEntityName(a.ActionText))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var uniqueNames = entityNames.Distinct().ToList();
        var isSingleEntity = uniqueNames.Count == 1;
        var namesList = FormatNamesList(uniqueNames);

        // Files, Images, Documents
        if ((entityType == "File" || entityType == "Image" || entityType == "Document") && actionType == ActivityActionKind.Created)
        {
            var inProject = first.ActionText.Contains("в проект");
            var label = entityType switch
            {
                "Image" => "изображение",
                "Document" => "документ",
                _ => "файл"
            };
            var labelForm2 = entityType switch
            {
                "Image" => "изображения",
                "Document" => "документа",
                _ => "файла"
            };
            var labelForm5 = entityType switch
            {
                "Image" => "изображений",
                "Document" => "документов",
                _ => "файлов"
            };
            var filesForm = GetPluralForm(count, label, labelForm2, labelForm5);
            if (inProject && !string.IsNullOrEmpty(context))
                return $"Загружено {count} {filesForm} в проект «{context}»";
            return $"Загружено {count} {filesForm}";
        }

        // Messages
        if (entityType == "Message" && actionType == ActivityActionKind.Message)
        {
            var messagesForm = GetPluralForm(count, "сообщение", "сообщения", "сообщений");
            if (!string.IsNullOrEmpty(context))
                return $"{count} {messagesForm} в {context}";
            return $"{count} {messagesForm}";
        }

        // Projects
        if (entityType == "Project")
        {
            if (isSingleEntity && count > 1)
            {
                var singleActionText = actionType switch
                {
                    ActivityActionKind.Created => "создан",
                    ActivityActionKind.Updated => "обновлён",
                    ActivityActionKind.Deleted => "удалён",
                    _ => "действие с"
                };
                var timesForm = GetPluralForm(count, "раз", "раза", "раз");
                return $"Проект «{uniqueNames[0]}» {singleActionText} {count} {timesForm}";
            }

            var actionText = actionType switch
            {
                ActivityActionKind.Created => "Создан",
                ActivityActionKind.Updated => "Обновлён",
                ActivityActionKind.Deleted => "Удалён",
                _ => "Действие с"
            };

            var projectsForm = GetPluralForm(count, "проект", "проекта", "проектов");
            if (!string.IsNullOrEmpty(namesList))
                return $"{actionText} {projectsForm}: {namesList}";
            return $"{actionText} {count} {projectsForm}";
        }

        // Tasks
        if (entityType == "Task")
        {
            if (isSingleEntity && count > 1)
            {
                var singleActionText = actionType switch
                {
                    ActivityActionKind.Created => "создана",
                    ActivityActionKind.Updated => "обновлена",
                    ActivityActionKind.Deleted => "удалена",
                    ActivityActionKind.MarkedForDeletion => "пометка удаления",
                    ActivityActionKind.UnmarkedForDeletion => "снята пометка",
                    _ => "действие"
                };

                var timesForm = GetPluralForm(count, "раз", "раза", "раз");
                if (!string.IsNullOrEmpty(context))
                    return $"Задача «{uniqueNames[0]}» в проекте «{context}» {singleActionText} {count} {timesForm}";
                return $"Задача «{uniqueNames[0]}» {singleActionText} {count} {timesForm}";
            }

            var actionText = actionType switch
            {
                ActivityActionKind.Created => "Создана",
                ActivityActionKind.Updated => "Обновлена",
                ActivityActionKind.Deleted => "Удалена",
                ActivityActionKind.MarkedForDeletion => "Пометка удаления",
                ActivityActionKind.UnmarkedForDeletion => "Снята пометка",
                _ => "Действие с"
            };

            var tasksForm = GetPluralForm(count, "задача", "задачи", "задач");
            if (!string.IsNullOrEmpty(namesList))
            {
                if (!string.IsNullOrEmpty(context))
                    return $"{actionText} {tasksForm} в проекте «{context}»: {namesList}";
                return $"{actionText} {tasksForm}: {namesList}";
            }

            if (!string.IsNullOrEmpty(context))
                return $"{actionText} {count} {tasksForm} в проекте «{context}»";
            return $"{actionText} {count} {tasksForm}";
        }

        // Stages
        if (entityType == "Stage")
        {
            if (isSingleEntity && count > 1)
            {
                var singleActionText = actionType switch
                {
                    ActivityActionKind.Created => "создан",
                    ActivityActionKind.Updated => "обновлён",
                    ActivityActionKind.Deleted => "удалён",
                    _ => "действие"
                };

                var timesForm = GetPluralForm(count, "раз", "раза", "раз");
                if (!string.IsNullOrEmpty(context))
                    return $"Этап «{uniqueNames[0]}» в задаче «{context}» {singleActionText} {count} {timesForm}";
                return $"Этап «{uniqueNames[0]}» {singleActionText} {count} {timesForm}";
            }

            var actionText = actionType switch
            {
                ActivityActionKind.Created => "Создан",
                ActivityActionKind.Updated => "Обновлён",
                ActivityActionKind.Deleted => "Удалён",
                _ => "Действие с"
            };

            var stagesForm = GetPluralForm(count, "этап", "этапа", "этапов");
            if (!string.IsNullOrEmpty(namesList))
            {
                if (!string.IsNullOrEmpty(context))
                    return $"{actionText} {stagesForm} в задаче «{context}»: {namesList}";
                return $"{actionText} {stagesForm}: {namesList}";
            }

            if (!string.IsNullOrEmpty(context))
                return $"{actionText} {count} {stagesForm} в задаче «{context}»";
            return $"{actionText} {count} {stagesForm}";
        }

        // Users
        if (entityType == "User")
        {
            if (isSingleEntity && count > 1)
            {
                var singleActionText = actionType switch
                {
                    ActivityActionKind.UserCreated => "создан",
                    ActivityActionKind.UserEdited => "изменён",
                    ActivityActionKind.UserDeleted => "удалён",
                    ActivityActionKind.UserBlocked => "заблокирован",
                    ActivityActionKind.UserUnblocked => "разблокирован",
                    _ => "действие"
                };

                var timesForm = GetPluralForm(count, "раз", "раза", "раз");
                return $"Пользователь «{uniqueNames[0]}» {singleActionText} {count} {timesForm}";
            }

            var actionText = actionType switch
            {
                ActivityActionKind.UserCreated => "Создан",
                ActivityActionKind.UserEdited => "Изменён",
                ActivityActionKind.UserDeleted => "Удалён",
                ActivityActionKind.UserBlocked => "Заблокирован",
                ActivityActionKind.UserUnblocked => "Разблокирован",
                _ => "Действие с"
            };

            var usersForm = GetPluralForm(count, "пользователь", "пользователя", "пользователей");
            if (!string.IsNullOrEmpty(namesList))
                return $"{actionText} {usersForm}: {namesList}";
            return $"{actionText} {count} {usersForm}";
        }

        // Default fallback
        var actionsForm = GetPluralForm(count, "действие", "действия", "действий");
        return $"{count} {actionsForm}";
    }

    /// <summary>Extracts context (task/project name) from action text.</summary>
    private static string ExtractContextFromActionText(string actionText)
    {
        // Extract from "в задаче «Имя»" or "в проекте «Имя»" or "в обсуждении проекта «Имя»"
        if (actionText.Contains("в задаче «"))
        {
            var start = actionText.IndexOf("в задаче «") + 10;
            var end = actionText.IndexOf("»", start);
            if (start > 9 && end > start)
                return $"задаче «{actionText.Substring(start, end - start)}»";
        }
        else if (actionText.Contains("в проекте «"))
        {
            var start = actionText.IndexOf("в проекте «") + 11;
            var end = actionText.IndexOf("»", start);
            if (start > 10 && end > start)
                return $"проекте «{actionText.Substring(start, end - start)}»";
        }
        else if (actionText.Contains("в обсуждении проекта «"))
        {
            var start = actionText.IndexOf("в обсуждении проекта «") + 21;
            var end = actionText.IndexOf("»", start);
            if (start > 20 && end > start)
                return $"обсуждении проекта «{actionText.Substring(start, end - start)}»";
        }
        return string.Empty;
    }

    /// <summary>Extracts entity name from action text.</summary>
    private static string ExtractEntityName(string actionText)
    {
        // Extract from "Создан проект «Имя»" or "Задача «Имя»" etc.
        var start = actionText.IndexOf("«");
        var end = actionText.IndexOf("»");
        if (start > 0 && end > start)
            return actionText.Substring(start + 1, end - start - 1);
        return string.Empty;
    }

    /// <summary>Formats a list of names, showing up to 4 names then truncating.</summary>
    private static string FormatNamesList(List<string> names)
    {
        if (names.Count == 0) return string.Empty;
        if (names.Count == 1) return $"«{names[0]}»";

        const int maxNamesToShow = 4;
        var result = new StringBuilder();

        for (int i = 0; i < Math.Min(names.Count, maxNamesToShow); i++)
        {
            if (result.Length > 0)
                result.Append(", ");
            result.Append($"«{names[i]}»");
        }

        if (names.Count > maxNamesToShow)
            result.Append(" ...");

        return result.ToString();
    }

    /// <summary>Returns the correct plural form for a Russian noun based on count.</summary>
    private static string GetPluralForm(int count, string form1, string form2, string form5)
    {
        var lastTwo = count % 100;
        var lastOne = count % 10;

        if (lastTwo >= 11 && lastTwo <= 19)
            return form5;
        if (lastOne == 1)
            return form1;
        if (lastOne >= 2 && lastOne <= 4)
            return form2;
        return form5;
    }

    private static bool PassesRoleFilter(
        LocalActivityLog a,
        string userRole,
        Guid? currentUserId,
        HashSet<Guid>? managerVisibleIds,
        HashSet<Guid>? foremanVisibleIds,
        WorkerFilterData? workerFilterData)
    {
        if (IsManagerRole(userRole))
        {
            if (!currentUserId.HasValue || managerVisibleIds is null)
                return true;
            return a.UserId.HasValue && managerVisibleIds.Contains(a.UserId.Value);
        }

        if (IsForemanRole(userRole))
        {
            if (!currentUserId.HasValue || foremanVisibleIds is null)
                return true;
            return a.UserId.HasValue && foremanVisibleIds.Contains(a.UserId.Value);
        }

        if (IsWorkerRole(userRole))
        {
            if (!currentUserId.HasValue || workerFilterData is null)
                return true;

            // Worker sees their own actions
            if (a.UserId == currentUserId.Value)
                return true;

            // Worker sees collaborator actions only if related to their tasks/stages
            if (a.UserId.HasValue && workerFilterData.CollaboratorIds.Contains(a.UserId.Value))
            {
                return IsActivityRelatedToWorkerTasks(a, workerFilterData);
            }

            return false;
        }

        if (currentUserId.HasValue)
            return a.UserId == currentUserId.Value;

        return true;
    }

    /// <summary>Checks if an activity is related to worker's tasks/stages (for foreman actions).</summary>
    private static bool IsActivityRelatedToWorkerTasks(LocalActivityLog a, WorkerFilterData workerData)
    {
        var entityType = a.EntityType;
        var entityId = a.EntityId;

        // Direct task/stage actions
        if (entityType == "Task" && workerData.TaskIds.Contains(entityId))
            return true;

        if (entityType == "Stage" && workerData.StageIds.Contains(entityId))
            return true;

        // File/Image/Document actions - check if file is linked to worker's tasks/stages
        if ((entityType == "File" || entityType == "Image" || entityType == "Document") 
            && workerData.RelatedFileIds.Contains(entityId))
            return true;

        // Message actions - check if message is in worker's tasks
        if (entityType == "Message" && workerData.RelatedMessageIds.Contains(entityId))
            return true;

        // Material/Equipment actions - check if linked to worker's tasks/stages
        // (These may need additional context from DetailsText or extended fields)
        // For now, only show direct task/stage/file actions

        return false;
    }

    private static async Task AttachAvatarsAsync(LocalDbContext db, List<LocalActivityLog> activities, CancellationToken ct)
    {
        var userIds = activities.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToList();
        if (userIds.Count == 0)
            return;

        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
            .ToDictionaryAsync(u => u.Id, ct);
        ct.ThrowIfCancellationRequested();

        foreach (var a in activities)
        {
            if (a.UserId.HasValue && userAvatars.TryGetValue(a.UserId.Value, out var av))
            {
                a.AvatarData = av.AvatarData;
                a.AvatarPath = av.AvatarPath;
            }
        }
    }

    /// <summary>Returns the count of activities visible to the current user (for stats display). Uses a reasonable limit.</summary>
    public static async Task<int> GetFilteredActivityCountAsync(
        LocalDbContext db, IAuthService auth, bool excludeAuthEvents = true, CancellationToken ct = default)
    {
        var activities = await GetFilteredActivitiesAsync(db, auth, 500, excludeAuthEvents, ct);
        return activities.Count;
    }

    public static bool IsAdminRole(string? role) =>
        !string.IsNullOrEmpty(role) &&
        (string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));

    private static bool IsManagerRole(string role) =>
        string.Equals(role, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsForemanRole(string role) =>
        string.Equals(role, "Foreman", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkerRole(string role) =>
        string.Equals(role, "Worker", StringComparison.OrdinalIgnoreCase);

    private static async Task<HashSet<Guid>> GetForemanVisibleUserIdsAsync(LocalDbContext db, Guid foremanUserId, CancellationToken ct)
    {
        var visible = new HashSet<Guid> { foremanUserId };

        var foremanProjectIds = await db.ProjectMembers
            .Where(m => m.UserId == foremanUserId)
            .Select(m => m.ProjectId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (foremanProjectIds.Count == 0)
            return visible;

        // Add project members
        var memberIds = await db.ProjectMembers
            .Where(m => foremanProjectIds.Contains(m.ProjectId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in memberIds) visible.Add(id);

        // Add project managers (ManagerId from LocalProject)
        var managerIds = await db.Projects
            .Where(p => foremanProjectIds.Contains(p.Id))
            .Select(p => p.ManagerId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in managerIds) visible.Add(id);

        var taskIds = await db.Tasks
            .Where(t => foremanProjectIds.Contains(t.ProjectId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (taskIds.Count > 0)
        {
            var taskAssigneeIds = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId))
                .Select(ta => ta.UserId)
                .ToListAsync(ct);
            foreach (var id in taskAssigneeIds) visible.Add(id);

            var stageIds = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId))
                .Select(s => s.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (stageIds.Count > 0)
            {
                var stageAssigneeIds = await db.StageAssignees
                    .Where(sa => stageIds.Contains(sa.StageId))
                    .Select(sa => sa.UserId)
                    .ToListAsync(ct);
                foreach (var id in stageAssigneeIds) visible.Add(id);
            }
        }

        return visible;
    }

    /// <summary>Gets visible user IDs for manager: self + project members + admins in shared projects/tasks/stages.</summary>
    private static async Task<HashSet<Guid>> GetManagerVisibleUserIdsAsync(LocalDbContext db, Guid managerUserId, CancellationToken ct)
    {
        var visible = new HashSet<Guid> { managerUserId };

        // Get projects where manager is the manager
        var managerProjectIds = await db.Projects
            .Where(p => p.ManagerId == managerUserId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (managerProjectIds.Count == 0)
            return visible;

        // Add project members
        var memberIds = await db.ProjectMembers
            .Where(m => managerProjectIds.Contains(m.ProjectId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in memberIds) visible.Add(id);

        var taskIds = await db.Tasks
            .Where(t => managerProjectIds.Contains(t.ProjectId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (taskIds.Count > 0)
        {
            var taskAssigneeIds = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId))
                .Select(ta => ta.UserId)
                .ToListAsync(ct);
            foreach (var id in taskAssigneeIds) visible.Add(id);

            var stageIds = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId))
                .Select(s => s.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (stageIds.Count > 0)
            {
                var stageAssigneeIds = await db.StageAssignees
                    .Where(sa => stageIds.Contains(sa.StageId))
                    .Select(sa => sa.UserId)
                    .ToListAsync(ct);
                foreach (var id in stageAssigneeIds) visible.Add(id);
            }
        }

        // Add admins who are members of manager's projects
        var adminMemberIds = await db.ProjectMembers
            .Join(db.Users, pm => pm.UserId, u => u.Id, (pm, u) => new { pm.ProjectId, pm.UserId, u.RoleName })
            .Where(x => managerProjectIds.Contains(x.ProjectId) && IsAdminRole(x.RoleName))
            .Select(x => x.UserId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in adminMemberIds) visible.Add(id);

        return visible;
    }

    /// <summary>Gets all filter data for worker: task/stage IDs, collaborator user IDs, and related file IDs.</summary>
    private static async Task<WorkerFilterData> GetWorkerFilterDataAsync(
        LocalDbContext db, Guid workerUserId, CancellationToken ct)
    {
        var data = new WorkerFilterData();

        // Get task and stage IDs where worker is assigned
        var (taskIds, stageIds) = await GetWorkerVisibleTaskStageIdsAsync(db, workerUserId, ct);
        data.TaskIds = taskIds;
        data.StageIds = stageIds;

        // Get user IDs who work on the same tasks/stages (strict - only task/stage assignees)
        data.CollaboratorIds = await GetWorkerVisibleCollaboratorIdsAsync(db, taskIds, stageIds, ct);

        // Get file IDs linked to worker's tasks/stages
        if (taskIds.Count > 0 || stageIds.Count > 0)
        {
            var relatedFileIds = await db.Files
                .Where(f => (f.TaskId.HasValue && taskIds.Contains(f.TaskId.Value)) ||
                           (f.StageId.HasValue && stageIds.Contains(f.StageId.Value)))
                .Select(f => f.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            data.RelatedFileIds = relatedFileIds.ToHashSet();
        }

        // Get message IDs linked to worker's tasks/projects
        if (taskIds.Count > 0)
        {
            var relatedMessageIds = await db.Messages
                .Where(m => m.TaskId.HasValue && taskIds.Contains(m.TaskId.Value))
                .Select(m => m.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            data.RelatedMessageIds = relatedMessageIds.ToHashSet();
        }

        return data;
    }

    /// <summary>Gets task and stage IDs where the worker is assigned (directly or via task assignees).</summary>
    private static async Task<(HashSet<Guid> TaskIds, HashSet<Guid> StageIds)> GetWorkerVisibleTaskStageIdsAsync(
        LocalDbContext db, Guid workerUserId, CancellationToken ct)
    {
        var taskIds = new HashSet<Guid>();
        var stageIds = new HashSet<Guid>();

        // Tasks where worker is directly assigned
        var directTaskIds = await db.Tasks
            .Where(t => t.AssignedUserId == workerUserId)
            .Select(t => t.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in directTaskIds) taskIds.Add(id);

        // Tasks where worker is in TaskAssignees
        var assigneeTaskIds = await db.TaskAssignees
            .Where(ta => ta.UserId == workerUserId)
            .Select(ta => ta.TaskId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in assigneeTaskIds) taskIds.Add(id);

        // Stages where worker is directly assigned
        var directStageIds = await db.TaskStages
            .Where(s => s.AssignedUserId == workerUserId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in directStageIds) stageIds.Add(id);

        // Stages where worker is in StageAssignees
        var assigneeStageIds = await db.StageAssignees
            .Where(sa => sa.UserId == workerUserId)
            .Select(sa => sa.StageId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in assigneeStageIds) stageIds.Add(id);

        // Also include stages from worker's tasks
        if (taskIds.Count > 0)
        {
            var taskStageIds = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId))
                .Select(s => s.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            foreach (var id in taskStageIds) stageIds.Add(id);
        }

        return (taskIds, stageIds);
    }

    /// <summary>Gets all user IDs who work on the same tasks or stages as the worker.</summary>
    private static async Task<HashSet<Guid>> GetWorkerVisibleCollaboratorIdsAsync(
        LocalDbContext db, HashSet<Guid> workerTaskIds, HashSet<Guid> workerStageIds, CancellationToken ct)
    {
        var foremanIds = new HashSet<Guid>();

        // Get all user IDs assigned to worker's tasks (including foremen)
        if (workerTaskIds.Count > 0)
        {
            var taskAssigneeIds = await db.TaskAssignees
                .Where(ta => workerTaskIds.Contains(ta.TaskId))
                .Select(ta => ta.UserId)
                .Distinct()
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();

            foreach (var id in taskAssigneeIds) foremanIds.Add(id);

            // Also check direct AssignedUserId on tasks
            var taskAssignedIds = await db.Tasks
                .Where(t => workerTaskIds.Contains(t.Id) && t.AssignedUserId.HasValue)
                .Select(t => t.AssignedUserId!.Value)
                .Distinct()
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();

            foreach (var id in taskAssignedIds) foremanIds.Add(id);
        }

        // Get all user IDs assigned to worker's stages (including foremen)
        if (workerStageIds.Count > 0)
        {
            var stageAssigneeIds = await db.StageAssignees
                .Where(sa => workerStageIds.Contains(sa.StageId))
                .Select(sa => sa.UserId)
                .Distinct()
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();

            foreach (var id in stageAssigneeIds) foremanIds.Add(id);

            // Also check direct AssignedUserId on stages
            var stageAssignedIds = await db.TaskStages
                .Where(s => workerStageIds.Contains(s.Id) && s.AssignedUserId.HasValue)
                .Select(s => s.AssignedUserId!.Value)
                .Distinct()
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();

            foreach (var id in stageAssignedIds) foremanIds.Add(id);
        }

        return foremanIds;
    }
}
