using MPMS.Models;

namespace MPMS.Services;

/// <summary>Единая формула прогресса: дробные проценты, зависимость от распределения по статусам.</summary>
public static class ProgressCalculator
{
    /// <summary>
    /// Прогресс по этапам/задачам. Формула зависит от количества в каждом статусе:
    /// - Completed = 1.0
    /// - InProgress = 0.55 + 0.05 * min(I, 5) — больше веса при нескольких в работе
    /// - Planned = 0.08 + 0.02 * min(P, 3) — небольшой вклад планирования
    /// Результат: дробный процент 0..100, округление до 1 знака при отображении.
    /// </summary>
    public static double GetProgressPercent(int completed, int inProgress, int planned, int total)
    {
        if (total <= 0) return 0;
        double wC = 1.0;
        double wI = 0.55 + 0.05 * Math.Min(inProgress, 5);
        double wP = 0.08 + 0.02 * Math.Min(planned, 3);
        double raw = (completed * wC + inProgress * wI + planned * wP) / total * 100;
        return Math.Min(100, Math.Round(raw, 0)); // целые проценты: 37, 83 и т.д.
    }

    /// <summary>Прогресс задачи по этапам (Completed, InProgress, Planned).</summary>
    public static double GetTaskProgressPercent(int completedStages, int inProgressStages, int totalStages)
    {
        int planned = Math.Max(0, totalStages - completedStages - inProgressStages);
        return GetProgressPercent(completedStages, inProgressStages, planned, totalStages);
    }

    /// <summary>Прогресс проекта по задачам (Completed, InProgress, Planned+Paused).</summary>
    public static double GetProjectProgressPercent(int completedTasks, int inProgressTasks, int totalTasks)
    {
        int planned = Math.Max(0, totalTasks - completedTasks - inProgressTasks);
        return GetProgressPercent(completedTasks, inProgressTasks, planned, totalTasks);
    }

    /// <summary>Строка для отображения: "67.5%" или "100%"</summary>
    public static string FormatPercent(double value)
    {
        return value % 1 == 0 ? $"{(int)value}%" : $"{value:F1}%";
    }
}
