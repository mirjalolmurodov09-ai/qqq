using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed record DashboardStats(
    int Groups, int Students, int Subjects, int TimetableEntries, int PlannedLessons, int ConductedLessons,
    double? AttendancePercent30Days, int Topics, int CompletedTopics, bool HasCalendar);

public sealed record LessonTopicInfo(int LessonId, string? TopicTitle);

public sealed class DashboardService
{
    private readonly IDbFactory _factory;
    public DashboardService(IDbFactory factory) => _factory = factory;

    public DashboardStats GetStats(UserSession session, DateOnly today)
    {
        using var db = _factory.Create();
        var groupIds = db.Groups.Where(g => !g.IsArchived).Select(g => g.Id).ToList();
        if (session.Role == UserRole.Observer)
        {
            var allowed = db.ObserverGroupAccess.Where(a => a.UserId == session.UserId).Select(a => a.GroupId).ToList();
            groupIds = groupIds.Where(allowed.Contains).ToList();
        }
        var from = today.AddDays(-30);
        var recent = db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Lesson!.Date >= from && a.Lesson.Date <= today && groupIds.Contains(a.Lesson.GroupId))
            .Select(a => a.Status).ToList();
        double? pct = recent.Count == 0 ? null
            : Math.Round(100.0 * recent.Count(s => s is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.LeftWithPermission) / recent.Count, 1);
        var lessons = db.LessonOccurrences.AsNoTracking().Where(l => groupIds.Contains(l.GroupId));
        return new DashboardStats(
            groupIds.Count,
            db.Students.Count(s => s.IsActive && groupIds.Contains(s.GroupId)),
            db.Subjects.Count(),
            db.TimetableEntries.Count(e => e.IsActive),
            lessons.Count(l => l.Status == LessonStatus.Planned && l.Date >= today),
            lessons.Count(l => l.Status == LessonStatus.Conducted),
            pct,
            db.CurriculumTopics.Count(),
            db.CurriculumTopics.Count(t => t.Status == TopicStatus.Completed),
            db.AcademicCalendars.Any(c => c.IsActive));
    }

    /// <summary>Darslarga joylashtirilgan mavzular (bosh sahifa va davomat uchun).</summary>
    public Dictionary<int, string> TopicsForLessons(IEnumerable<int> lessonIds)
    {
        var ids = lessonIds.ToList();
        using var db = _factory.Create();
        return db.TopicAssignments.AsNoTracking().Include(a => a.Topic)
            .Where(a => ids.Contains(a.LessonOccurrenceId)).ToList()
            .GroupBy(a => a.LessonOccurrenceId)
            .ToDictionary(g => g.Key, g => string.Join("; ", g.OrderBy(a => a.Topic!.OrderNo).Select(a => $"{a.Topic!.OrderNo}. {a.Topic.Title}")));
    }
}
