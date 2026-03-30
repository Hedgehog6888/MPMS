using System.Windows.Controls;

namespace MPMS.Infrastructure;

/// <summary>
/// Срок не раньше «сегодня»: прошлые дни через BlackoutDates (видимы, приглушены).
/// DisplayDateStart = 1-е число месяца границы — без пустых ячеек и с запретом листать календарь назад на прошлые месяцы.
/// </summary>
public static class DueDatePickerRestrictions
{
    public static void AttachNoPastSelectableBlackout(DatePicker picker)
    {
        void Refresh(object? sender = null, object? args = null)
        {
            picker.BlackoutDates.Clear();
            var today = DateTime.Today;
            var monthFloor = new DateTime(today.Year, today.Month, 1);

            if (picker.SelectedDate is DateTime sel && sel.Date < today)
            {
                // Редактирование со сроком в прошлом: не блокируем дни, но не даём уйти месяцами раньше этого месяца.
                var d = sel.Date;
                picker.DisplayDateStart = new DateTime(d.Year, d.Month, 1);
                return;
            }

            picker.DisplayDateStart = monthFloor;
            var yesterday = today.AddDays(-1);
            if (yesterday >= new DateTime(1900, 1, 1))
                picker.BlackoutDates.Add(new CalendarDateRange(new DateTime(1900, 1, 1), yesterday));
        }

        picker.Loaded += (_, _) => Refresh();
        picker.SelectedDateChanged += (_, _) => Refresh();
    }

    /// <summary>Нижняя граница навигации по месяцам — 1-е число месяца указанной даты.</summary>
    public static void SetDisplayDateStartFirstOfMonth(DatePicker picker, DateTime dateInMonth)
        => picker.DisplayDateStart = new DateTime(dateInMonth.Year, dateInMonth.Month, 1);
}
