using System.Text;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed record LessonTopicMaterials(CurriculumTopic Topic, IReadOnlyList<TopicMaterial> Materials);

public sealed class LessonContext
{
    public required LessonOccurrence Lesson { get; init; }
    public List<LessonTopicMaterials> Topics { get; } = new();
    public LessonLog? Log { get; init; }
    public int Students { get; init; }
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Marked { get; init; }
}

/// <summary>Dars rejimi: joriy dars konteksti, materiallar, dars jurnali va dars hisoboti.</summary>
public sealed class LessonService
{
    private readonly IDbFactory _factory;
    private readonly Func<DateTime> _utcNow;

    public LessonService(IDbFactory factory, Func<DateTime>? utcNow = null)
    {
        _factory = factory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public LessonContext GetContext(UserSession session, int lessonId)
    {
        session.Demand(Permission.ViewReports);
        using var db = _factory.Create();
        var lesson = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject).FirstOrDefault(l => l.Id == lessonId)
                     ?? throw new BusinessRuleException("Dars topilmadi.");
        if (session.Role == UserRole.Observer && !db.ObserverGroupAccess.Any(a => a.UserId == session.UserId && a.GroupId == lesson.GroupId))
            throw new AccessDeniedException("Bu guruhni ko'rishga ruxsat yo'q.");
        var topicIds = db.TopicAssignments.AsNoTracking().Where(a => a.LessonOccurrenceId == lessonId).Select(a => a.CurriculumTopicId).ToList();
        var topics = db.CurriculumTopics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).OrderBy(t => t.OrderNo).ToList();
        var materials = db.TopicMaterials.AsNoTracking().Where(m => topicIds.Contains(m.CurriculumTopicId)).OrderBy(m => m.Id).ToList();
        var records = db.AttendanceRecords.AsNoTracking().Where(a => a.LessonOccurrenceId == lessonId).Select(a => a.Status).ToList();
        var ctx = new LessonContext
        {
            Lesson = lesson,
            Log = db.LessonLogs.AsNoTracking().FirstOrDefault(l => l.LessonOccurrenceId == lessonId),
            Students = db.Students.Count(s => s.GroupId == lesson.GroupId && s.IsActive),
            Marked = records.Count,
            Present = records.Count(s => s is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.LeftWithPermission),
            Absent = records.Count(s => s is AttendanceStatus.AbsentExcused or AttendanceStatus.AbsentUnexcused),
        };
        foreach (var t in topics) ctx.Topics.Add(new LessonTopicMaterials(t, materials.Where(m => m.CurriculumTopicId == t.Id).ToList()));
        return ctx;
    }

    public LessonLog SaveLog(UserSession session, int lessonId, string? notes, string? homework, string? summary, string? pollResults)
    {
        session.Demand(Permission.TakeAttendance);
        using var db = _factory.Create();
        var lesson = db.LessonOccurrences.Find(lessonId) ?? throw new BusinessRuleException("Dars topilmadi.");
        if (session.Role == UserRole.Teacher && lesson.TeacherUserId != session.UserId)
            throw new AccessDeniedException("Faqat o'z darsingiz jurnalini yuritishingiz mumkin.");
        var log = db.LessonLogs.FirstOrDefault(l => l.LessonOccurrenceId == lessonId);
        if (log is null) { log = new LessonLog { LessonOccurrenceId = lessonId }; db.LessonLogs.Add(log); }
        log.Notes = Clean(notes); log.Homework = Clean(homework); log.Summary = Clean(summary); log.PollResults = Clean(pollResults);
        log.UpdatedByUserId = session.UserId;
        log.UpdatedUtc = _utcNow();
        db.Audit(session, "Dars jurnali saqlandi", nameof(LessonLog), lessonId, $"{lesson.Date:dd.MM.yyyy}");
        db.SaveChanges();
        return log;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public TopicMaterial AddMaterial(UserSession session, int topicId, string title, string location)
    {
        session.Demand(Permission.EditCurriculum);
        if (string.IsNullOrWhiteSpace(location)) throw new BusinessRuleException("Fayl yoki havolani ko'rsating.");
        location = location.Trim();
        var isLink = location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!isLink && !File.Exists(location)) throw new BusinessRuleException("Fayl topilmadi.");
        if (isLink && !Uri.TryCreate(location, UriKind.Absolute, out _)) throw new BusinessRuleException("Havola noto'g'ri.");
        using var db = _factory.Create();
        if (!db.CurriculumTopics.Any(t => t.Id == topicId)) throw new BusinessRuleException("Mavzu topilmadi.");
        var m = new TopicMaterial
        {
            CurriculumTopicId = topicId, Location = location, IsLink = isLink, CreatedUtc = _utcNow(),
            Title = string.IsNullOrWhiteSpace(title) ? (isLink ? location : Path.GetFileName(location)) : title.Trim(),
        };
        db.TopicMaterials.Add(m);
        db.SaveChanges();
        db.Audit(session, "Mavzuga material qo'shildi", nameof(TopicMaterial), m.Id, m.Title);
        db.SaveChanges();
        return m;
    }

    public void DeleteMaterial(UserSession session, int materialId)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        var m = db.TopicMaterials.Find(materialId);
        if (m is null) return;
        db.TopicMaterials.Remove(m);
        db.Audit(session, "Material o'chirildi", nameof(TopicMaterial), materialId, m.Title);
        db.SaveChanges();
    }

    /// <summary>Dars hisoboti (belgilangan matn — Word/PDF ga chiqariladi).</summary>
    public string ReportText(UserSession session, int lessonId)
    {
        var c = GetContext(session, lessonId);
        var l = c.Lesson;
        var sb = new StringBuilder();
        sb.AppendLine($"# Dars hisoboti");
        sb.AppendLine($"**Sana:** {l.Date:dd.MM.yyyy}, {l.Date.DayOfWeek.ToUz()}, {l.LessonNumber}-dars ({l.StartTime:HH\\:mm}–{l.EndTime:HH\\:mm})");
        sb.AppendLine($"**Guruh:** {l.Group?.Name}   **Fan:** {l.Subject?.Name}" + (string.IsNullOrWhiteSpace(l.Room) ? "" : $"   **Xona:** {l.Room}"));
        sb.AppendLine($"**Holat:** {l.Status.ToUz()}");
        sb.AppendLine();
        sb.AppendLine("# Mavzu");
        if (c.Topics.Count == 0) sb.AppendLine("KTR mavzusi joylashtirilmagan.");
        foreach (var t in c.Topics)
        {
            sb.AppendLine($"- {t.Topic.OrderNo}. {t.Topic.Title} ({t.Topic.Hours} soat, {t.Topic.Type.ToUz()}) — {t.Topic.Status.ToUz()}");
            if (!string.IsNullOrWhiteSpace(t.Topic.ExpectedOutcome)) sb.AppendLine($"  Kutilayotgan natija: {t.Topic.ExpectedOutcome}");
        }
        sb.AppendLine();
        sb.AppendLine("# Davomat");
        sb.AppendLine(c.Marked == 0 ? "Davomat olinmagan." : $"Guruhda {c.Students} o'quvchi; darsda: {c.Present}, kelmagan: {c.Absent}, belgilangan: {c.Marked}.");
        if (c.Log is { } log)
        {
            if (log.Homework is not null) { sb.AppendLine(); sb.AppendLine("# Uyga vazifa"); sb.AppendLine(log.Homework); }
            if (log.PollResults is not null) { sb.AppendLine(); sb.AppendLine("# Tezkor so'rov natijalari"); sb.AppendLine(log.PollResults); }
            if (log.Summary is not null) { sb.AppendLine(); sb.AppendLine("# Dars yakuni xulosasi"); sb.AppendLine(log.Summary); }
            if (log.Notes is not null) { sb.AppendLine(); sb.AppendLine("# O'qituvchi izohi"); sb.AppendLine(log.Notes); }
        }
        return sb.ToString();
    }
}

/// <summary>Tezkor so'rov (sinfda qo'l ko'tarish bilan): variantlar bo'yicha sanoq va natija matni.</summary>
public static class QuickPoll
{
    public static string Format(string question, IReadOnlyList<(string Option, int Count)> counts, string? correct = null)
    {
        var total = counts.Sum(c => c.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"Savol: {question}");
        foreach (var (opt, n) in counts)
        {
            var pct = total == 0 ? 0 : Math.Round(100.0 * n / total);
            sb.AppendLine($"- {opt}: {n} ({pct}%)" + (correct is not null && opt == correct ? " — to'g'ri javob" : ""));
        }
        sb.Append($"Jami javob: {total}");
        return sb.ToString();
    }
}
