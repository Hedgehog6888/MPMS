using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MPMS.Data;

/// <summary>
/// SQLite возвращает DateTime с Kind = Unspecified, из‑за этого UTC из БД ошибочно показывался как «локальное» время без сдвига.
/// При записи нормализуем в UTC, при чтении помечаем как Utc — дальше UI может вызывать ToLocalTime().
/// </summary>
internal sealed class DateTimeAsUtcConverter : ValueConverter<DateTime, DateTime>
{
    public DateTimeAsUtcConverter() : base(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}

internal sealed class NullableDateTimeAsUtcConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableDateTimeAsUtcConverter() : base(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    {
    }
}
