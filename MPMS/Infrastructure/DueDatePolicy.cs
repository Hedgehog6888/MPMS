namespace MPMS.Infrastructure;

/// <summary>Правило: срок выполнения не может быть календарной датой раньше «сегодня» (локально).</summary>
public static class DueDatePolicy
{
    public const string PastNotAllowedMessage = "Срок не может быть раньше сегодняшнего дня.";

    public static DateOnly MinAllowed => DateOnly.FromDateTime(DateTime.Today);

    public static bool IsAllowed(DateOnly? dueDate) =>
        !dueDate.HasValue || dueDate.Value >= MinAllowed;
}
