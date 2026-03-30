namespace MPMS.API;

/// <summary>Срок выполнения — не раньше сегодняшнего дня (дата сервера).</summary>
public static class DueDatePolicy
{
    public const string PastNotAllowedMessage = "Срок не может быть раньше сегодняшнего дня.";

    public static DateOnly MinAllowed => DateOnly.FromDateTime(DateTime.Today);

    public static bool IsAllowed(DateOnly? dueDate) =>
        !dueDate.HasValue || dueDate.Value >= MinAllowed;
}
