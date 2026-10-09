using AiUstozPro.Domain;

namespace AiUstozPro.Application.Calendar;

/// <summary>
/// O'zbekistonning sanasi qonun bilan qat'iy belgilangan bayramlari.
/// Ramazon hayiti va Qurbon hayiti har yili oy taqvimi bo'yicha e'lon qilinadi,
/// shuningdek qo'shimcha dam olish kunlari va ko'chirilgan ish kunlari har yili alohida qaror
/// bilan belgilanadi — ular TAXMIN QILINMAYDI, administrator rasmiy manbadan kiritadi.
/// </summary>
public static class UzbekistanHolidays
{
    public const string Source = "O'zbekiston Respublikasi Mehnat kodeksi (sanasi qat'iy bayramlar)";

    public static readonly IReadOnlyList<(int Month, int Day, string Title)> FixedDates = new[]
    {
        (1, 1, "Yangi yil"),
        (3, 8, "Xalqaro xotin-qizlar kuni"),
        (3, 21, "Navro'z bayrami"),
        (5, 9, "Xotira va qadrlash kuni"),
        (9, 1, "Mustaqillik kuni"),
        (10, 1, "O'qituvchi va murabbiylar kuni"),
        (12, 8, "Konstitutsiya kuni"),
    };

    public const string VariableNotice =
        "Ramazon hayiti, Qurbon hayiti, qo'shimcha dam olish kunlari va ko'chirilgan ish kunlari har yili rasmiy qaror bilan e'lon qilinadi. Ularni \"Istisno qo'shish\" orqali qo'lda kiriting.";

    /// <summary>Kalendar oralig'iga tushadigan sanasi qat'iy bayramlar.</summary>
    public static List<CalendarException> FixedHolidaysFor(AcademicCalendar calendar)
    {
        var list = new List<CalendarException>();
        for (var year = calendar.StartDate.Year; year <= calendar.EndDate.Year; year++)
        {
            foreach (var (m, d, title) in FixedDates)
            {
                var date = new DateOnly(year, m, d);
                if (date < calendar.StartDate || date > calendar.EndDate) continue;
                list.Add(new CalendarException
                {
                    AcademicCalendarId = calendar.Id,
                    Kind = CalendarExceptionKind.Holiday,
                    StartDate = date,
                    EndDate = date,
                    Title = title,
                    IsConfirmed = true,
                    Source = Source,
                });
            }
        }
        return list;
    }
}
