using AiUstozPro.Domain;

namespace AiUstozPro.Application.Calendar;

public sealed record TimetableConflict(TimetableEntry Other, string Message);

/// <summary>Bir vaqtda bir o'qituvchi, bir xona yoki bir guruh uchun ziddiyatli darslarni aniqlaydi.</summary>
public static class TimetableConflictChecker
{
    public static List<TimetableConflict> Check(TimetableEntry candidate, IEnumerable<TimetableEntry> existing)
    {
        var list = new List<TimetableConflict>();
        if (candidate.EndTime <= candidate.StartTime)
        {
            list.Add(new TimetableConflict(candidate, "Tugash vaqti boshlanish vaqtidan keyin bo'lishi kerak."));
            return list;
        }
        foreach (var e in existing)
        {
            if (e.Id != 0 && e.Id == candidate.Id) continue;
            if (!e.IsActive || e.AcademicCalendarId != candidate.AcademicCalendarId) continue;
            if (e.DayOfWeek != candidate.DayOfWeek) continue;
            if (!TimesOverlap(e, candidate) || !PeriodsOverlap(e, candidate)) continue;

            var when = $"{e.DayOfWeek.ToUz()}, {e.StartTime:HH\\:mm}–{e.EndTime:HH\\:mm}";
            if (e.TeacherUserId == candidate.TeacherUserId)
                list.Add(new TimetableConflict(e, $"O'qituvchi shu vaqtda band: {when}."));
            if (e.GroupId == candidate.GroupId)
                list.Add(new TimetableConflict(e, $"Guruhda shu vaqtda boshqa dars bor: {when}."));
            if (!string.IsNullOrWhiteSpace(e.Room) && string.Equals(e.Room?.Trim(), candidate.Room?.Trim(), StringComparison.OrdinalIgnoreCase))
                list.Add(new TimetableConflict(e, $"{e.Room} xonasi shu vaqtda band: {when}."));
        }
        return list;
    }

    private static bool TimesOverlap(TimetableEntry a, TimetableEntry b)
        => a.StartTime < b.EndTime && b.StartTime < a.EndTime;

    private static bool PeriodsOverlap(TimetableEntry a, TimetableEntry b)
    {
        var aFrom = a.ValidFrom ?? DateOnly.MinValue; var aTo = a.ValidTo ?? DateOnly.MaxValue;
        var bFrom = b.ValidFrom ?? DateOnly.MinValue; var bTo = b.ValidTo ?? DateOnly.MaxValue;
        return aFrom <= bTo && bFrom <= aTo;
    }
}
