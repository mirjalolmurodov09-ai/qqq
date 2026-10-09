using AiUstozPro.Domain;

namespace AiUstozPro.Application.Calendar;

/// <summary>Avtomatik hisoblangan bitta dars sanasi.</summary>
public sealed record GeneratedLesson(
    DateOnly Date,
    DayOfWeek ActualDayOfWeek,
    DayOfWeek ScheduleDayOfWeek,
    int TimetableEntryId,
    int GroupId,
    int SubjectId,
    int TeacherUserId,
    int LessonNumber,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Room,
    int AcademicHours,
    bool IsSkipped,
    string? SkipReason)
{
    /// <summary>Bir xil darsni aniqlash kaliti (jadval yozuvi + sana).</summary>
    public (int, DateOnly) Key => (TimetableEntryId, Date);
}

/// <summary>
/// Dars jadvali va o'quv kalendari asosida dars sanalarini hisoblaydi.
/// Sof funksiya: ma'lumotlar bazasiga bog'liq emas, to'liq unit-test qilinadi.
/// </summary>
public static class LessonDateGenerator
{
    /// <param name="includeSkipped">true bo'lsa bayram/ta'til tufayli o'tilmaydigan sanalar ham
    /// <see cref="GeneratedLesson.IsSkipped"/> = true bilan qaytariladi (ko'rib chiqish uchun).</param>
    public static IReadOnlyList<GeneratedLesson> Generate(
        AcademicCalendar calendar,
        IEnumerable<CalendarException> exceptions,
        IEnumerable<TimetableEntry> entries,
        bool includeSkipped = false)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (calendar.EndDate < calendar.StartDate)
            throw new ArgumentException("O'quv yilining tugash sanasi boshlanish sanasidan oldin bo'lishi mumkin emas.");

        var exList = exceptions.ToList();
        var active = entries.Where(e => e.IsActive).ToList();
        var result = new List<GeneratedLesson>();

        for (var d = calendar.StartDate; d <= calendar.EndDate; d = d.AddDays(1))
        {
            var actualDow = d.DayOfWeek;

            // 1. Ko'chirilgan ish kuni: shu sana boshqa hafta kuni jadvali bo'yicha ishlaydi.
            var transfer = exList.FirstOrDefault(x =>
                x.Kind == CalendarExceptionKind.TransferredWorkday && x.Covers(d) && x.WorksAsDayOfWeek.HasValue
                && x.GroupId is null && x.SubjectId is null);

            DayOfWeek scheduleDow;
            if (transfer is not null)
                scheduleDow = transfer.WorksAsDayOfWeek!.Value;
            else if (calendar.IsWorkingDay(actualDow))
                scheduleDow = actualDow;
            else
                continue; // dam olish kuni va istisno yo'q

            foreach (var entry in active.Where(e => e.DayOfWeek == scheduleDow)
                                        .OrderBy(e => e.LessonNumber).ThenBy(e => e.Id))
            {
                if (entry.ValidFrom is { } vf && d < vf) continue;
                if (entry.ValidTo is { } vt && d > vt) continue;

                var blocking = FindBlocking(exList, d, entry);
                if (blocking is not null)
                {
                    if (includeSkipped)
                        result.Add(Make(d, actualDow, scheduleDow, entry, true, Describe(blocking)));
                    continue;
                }
                result.Add(Make(d, actualDow, scheduleDow, entry, false, null));
            }
        }

        // Guruh yoki fanga xos ko'chirilgan ish kunlari (dam olish kunida maxsus dars).
        foreach (var x in exList.Where(x => x.Kind == CalendarExceptionKind.TransferredWorkday
                                            && x.WorksAsDayOfWeek.HasValue
                                            && (x.GroupId is not null || x.SubjectId is not null)))
        {
            for (var d = x.StartDate; d <= x.EndDate; d = d.AddDays(1))
            {
                if (d < calendar.StartDate || d > calendar.EndDate) continue;
                foreach (var entry in active.Where(e => e.DayOfWeek == x.WorksAsDayOfWeek!.Value && Applies(x, e)))
                {
                    if (entry.ValidFrom is { } vf && d < vf) continue;
                    if (entry.ValidTo is { } vt && d > vt) continue;
                    if (result.Any(r => r.TimetableEntryId == entry.Id && r.Date == d && !r.IsSkipped)) continue;
                    var blocking = FindBlocking(exList, d, entry);
                    if (blocking is not null)
                    {
                        if (includeSkipped)
                            result.Add(Make(d, d.DayOfWeek, x.WorksAsDayOfWeek!.Value, entry, true, Describe(blocking)));
                        continue;
                    }
                    result.Add(Make(d, d.DayOfWeek, x.WorksAsDayOfWeek!.Value, entry, false, null));
                }
            }
        }

        return result.OrderBy(r => r.Date).ThenBy(r => r.LessonNumber).ThenBy(r => r.TimetableEntryId).ToList();
    }

    /// <summary>Faqat dars o'tiladigan sanalar (o'tkazib yuborilganlarsiz).</summary>
    public static IReadOnlyList<GeneratedLesson> GenerateFor(
        AcademicCalendar calendar,
        IEnumerable<CalendarException> exceptions,
        IEnumerable<TimetableEntry> entries,
        int groupId,
        int subjectId)
        => Generate(calendar, exceptions, entries.Where(e => e.GroupId == groupId && e.SubjectId == subjectId))
            .Where(g => !g.IsSkipped).ToList();

    internal static bool Applies(CalendarException x, TimetableEntry e)
        => (x.GroupId is null || x.GroupId == e.GroupId) && (x.SubjectId is null || x.SubjectId == e.SubjectId);

    private static CalendarException? FindBlocking(List<CalendarException> exList, DateOnly d, TimetableEntry entry)
        => exList.FirstOrDefault(x =>
            x.Kind is CalendarExceptionKind.Holiday or CalendarExceptionKind.ExtraDayOff
                   or CalendarExceptionKind.Vacation or CalendarExceptionKind.NoLessons
            && x.Covers(d) && Applies(x, entry));

    private static string Describe(CalendarException x)
        => string.IsNullOrWhiteSpace(x.Title) ? x.Kind.ToUz() : $"{x.Kind.ToUz()}: {x.Title}";

    private static GeneratedLesson Make(DateOnly d, DayOfWeek actual, DayOfWeek schedule, TimetableEntry e, bool skipped, string? reason)
        => new(d, actual, schedule, e.Id, e.GroupId, e.SubjectId, e.TeacherUserId, e.LessonNumber,
               e.StartTime, e.EndTime, e.Room, e.AcademicHours, skipped, reason);
}
