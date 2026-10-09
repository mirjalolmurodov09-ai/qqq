using AiUstozPro.Domain;

namespace AiUstozPro.Application.Calendar;

/// <summary>Mavjud dars yozuvi haqida qayta hisoblash uchun zarur ma'lumot.</summary>
public sealed record ExistingLesson(
    int Id,
    int? TimetableEntryId,
    DateOnly Date,
    DateOnly? OriginalDate,
    LessonOrigin Origin,
    LessonStatus Status,
    bool IsConfirmed,
    bool HasAttendance,
    bool HasTopic)
{
    /// <summary>Foydalanuvchi qarori yoki tarixiy ma'lumot bor — avtomatik o'chirilmaydi.</summary>
    public bool IsProtected =>
        IsConfirmed || HasAttendance || Origin != LessonOrigin.Generated
        || Status is LessonStatus.Conducted or LessonStatus.Cancelled;
}

public sealed class ReconcileResult
{
    public List<GeneratedLesson> ToAdd { get; } = new();
    /// <summary>Faqat himoyalanmagan, avtomatik yaratilgan va endi kerak bo'lmagan darslar.</summary>
    public List<ExistingLesson> ToRemove { get; } = new();
    /// <summary>O'zgarishsiz qoladigan darslar.</summary>
    public List<ExistingLesson> Kept { get; } = new();
    /// <summary>Himoyalangan, lekin yangi kalendar bo'yicha bu sanada dars bo'lmasligi kerak — foydalanuvchi hal qiladi.</summary>
    public List<(ExistingLesson Lesson, string Message)> Conflicts { get; } = new();

    public bool HasChanges => ToAdd.Count > 0 || ToRemove.Count > 0;
}

/// <summary>
/// Qayta hisoblash: yangi hisoblangan sanalarni bazadagi mavjud darslar bilan solishtiradi.
/// Tasdiqlangan, davomat olingan, qo'lda qo'shilgan/ko'chirilgan va bekor qilingan darslar hech qachon
/// avtomatik o'chirilmaydi — ular faqat ziddiyat sifatida ko'rsatiladi.
/// </summary>
public static class OccurrenceReconciler
{
    public static ReconcileResult Reconcile(IEnumerable<ExistingLesson> existing, IEnumerable<GeneratedLesson> generated)
    {
        var gen = generated.Where(g => !g.IsSkipped).ToList();
        var genKeys = gen.Select(g => (g.TimetableEntryId, g.Date)).ToHashSet();
        var ex = existing.ToList();
        var result = new ReconcileResult();

        // Mavjud darslar "egallagan" kalitlar: ko'chirilgan dars asl sanasini egallaydi.
        var occupied = new HashSet<(int, DateOnly)>();
        foreach (var e in ex)
        {
            if (e.TimetableEntryId is not int tid) continue;
            if (e.Origin == LessonOrigin.Manual) continue;
            occupied.Add((tid, e.OriginalDate ?? e.Date));
        }

        foreach (var g in gen)
            if (!occupied.Contains((g.TimetableEntryId, g.Date)))
                result.ToAdd.Add(g);

        foreach (var e in ex)
        {
            if (e.Origin == LessonOrigin.Manual || e.TimetableEntryId is not int tid)
            {
                result.Kept.Add(e);
                continue;
            }
            var key = (tid, e.OriginalDate ?? e.Date);
            if (genKeys.Contains(key))
            {
                result.Kept.Add(e);
                continue;
            }
            if (e.IsProtected)
            {
                result.Kept.Add(e);
                // Ko'chirilgan darsning asl sanasi endi dam olish bo'lsa — bu tabiiy, ziddiyat emas.
                if (e.Origin == LessonOrigin.Moved) continue;
                if (e.Status == LessonStatus.Cancelled) continue;
                var why = e.HasAttendance ? "davomat olingan"
                        : e.Status == LessonStatus.Conducted ? "dars o'tilgan deb belgilangan"
                        : e.IsConfirmed ? "dars tasdiqlangan"
                        : "foydalanuvchi o'zgartirgan";
                result.Conflicts.Add((e, $"{e.Date:dd.MM.yyyy}: yangi kalendar bo'yicha bu kunda dars yo'q, lekin {why}. Yozuv saqlab qolindi — qo'lda hal qiling."));
            }
            else
            {
                result.ToRemove.Add(e);
            }
        }
        return result;
    }
}
