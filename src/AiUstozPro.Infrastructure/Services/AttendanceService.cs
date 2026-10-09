using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed class AttendanceSheetRow
{
    public required Student Student { get; init; }
    public AttendanceStatus? Status { get; set; }
    public string? Note { get; set; }
    public int? RecordId { get; init; }
}

public sealed record AttendanceMark(int StudentId, AttendanceStatus Status, string? Note);

public sealed record AttendanceSaveResult(int Added, int Changed, int Unchanged);

/// <summary>Qo'lda davomat. Bir o'quvchiga bir darsda faqat bitta yozuv (baza darajasida ham cheklangan).</summary>
public sealed class AttendanceService
{
    private readonly IDbFactory _factory;
    private readonly Func<DateOnly> _today;

    public AttendanceService(IDbFactory factory, Func<DateOnly>? today = null)
    {
        _factory = factory;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public (LessonOccurrence Lesson, List<AttendanceSheetRow> Rows) GetSheet(UserSession session, int lessonId)
    {
        using var db = _factory.Create();
        var lesson = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject)
                       .FirstOrDefault(l => l.Id == lessonId) ?? throw new BusinessRuleException("Dars topilmadi.");
        if (session.Role == UserRole.Observer &&
            !db.ObserverGroupAccess.Any(a => a.UserId == session.UserId && a.GroupId == lesson.GroupId))
            throw new AccessDeniedException("Bu guruhni ko'rishga ruxsat yo'q.");
        var records = db.AttendanceRecords.AsNoTracking().Where(a => a.LessonOccurrenceId == lessonId).ToList();
        var recordStudentIds = records.Select(r => r.StudentId).ToList();
        var students = db.Students.AsNoTracking()
            .Where(s => s.GroupId == lesson.GroupId && (s.IsActive || recordStudentIds.Contains(s.Id)))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.MiddleName).ToList();
        var rows = students.Select(s =>
        {
            var r = records.FirstOrDefault(x => x.StudentId == s.Id);
            return new AttendanceSheetRow { Student = s, Status = r?.Status, Note = r?.Note, RecordId = r?.Id };
        }).ToList();
        return (lesson, rows);
    }

    private static void DemandCanEdit(UserSession session, LessonOccurrence lesson)
    {
        session.Demand(Permission.TakeAttendance);
        if (session.Role == UserRole.Teacher && lesson.TeacherUserId != session.UserId)
            throw new AccessDeniedException("Faqat o'z darsingizda davomat olishingiz mumkin.");
    }

    /// <param name="correctionReason">Avval saqlangan yozuv o'zgartirilsa majburiy.</param>
    public AttendanceSaveResult Save(UserSession session, int lessonId, IEnumerable<AttendanceMark> marks, string? correctionReason)
    {
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        var lesson = db.LessonOccurrences.Find(lessonId) ?? throw new BusinessRuleException("Dars topilmadi.");
        DemandCanEdit(session, lesson);
        if (lesson.Status == LessonStatus.Cancelled) throw new BusinessRuleException("Dars bekor qilingan — davomat olinmaydi.");
        if (lesson.Date > _today()) throw new BusinessRuleException("Kelajakdagi dars uchun davomat olib bo'lmaydi.");

        var list = marks.ToList();
        var dupStudents = list.GroupBy(m => m.StudentId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupStudents.Count > 0) throw new BusinessRuleException("Bir o'quvchi ro'yxatda bir necha marta keldi.");
        var groupStudentIds = db.Students.Where(s => s.GroupId == lesson.GroupId).Select(s => s.Id).ToList().ToHashSet();
        if (list.Any(m => !groupStudentIds.Contains(m.StudentId)))
            throw new BusinessRuleException("Ro'yxatda bu guruhga tegishli bo'lmagan o'quvchi bor.");

        var existing = db.AttendanceRecords.Where(a => a.LessonOccurrenceId == lessonId).ToDictionary(a => a.StudentId);
        var changes = list.Where(m => existing.TryGetValue(m.StudentId, out var r) && (r.Status != m.Status || (r.Note ?? "") != (m.Note ?? ""))).ToList();
        if (changes.Count > 0 && string.IsNullOrWhiteSpace(correctionReason))
            throw new BusinessRuleException("Avval saqlangan davomat o'zgartirilmoqda — tuzatish sababini kiriting.");

        int added = 0, changed = 0, unchanged = 0;
        foreach (var m in list)
        {
            if (existing.TryGetValue(m.StudentId, out var r))
            {
                if (r.Status == m.Status && (r.Note ?? "") == (m.Note ?? "")) { unchanged++; continue; }
                var old = r.Status;
                r.Status = m.Status;
                r.Note = string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim();
                r.ModifiedUtc = DateTime.UtcNow;
                r.RecordedByUserId = session.UserId;
                db.Audit(session, "Davomat tuzatildi", nameof(AttendanceRecord), r.Id,
                    $"o'quvchi={m.StudentId}, dars={lessonId} ({lesson.Date:dd.MM.yyyy}): {old.ToUz()} → {m.Status.ToUz()}", correctionReason);
                changed++;
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    StudentId = m.StudentId, LessonOccurrenceId = lessonId, Status = m.Status,
                    Note = string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim(),
                    Method = "manual", IsConfirmed = m.Status != AttendanceStatus.Unidentified,
                    RecordedByUserId = session.UserId,
                });
                added++;
            }
        }
        if (added > 0 || changed > 0)
        {
            lesson.Status = LessonStatus.Conducted;
            lesson.IsConfirmed = true;
            if (added > 0)
                db.Audit(session, "Davomat olindi", nameof(LessonOccurrence), lessonId, $"{lesson.Date:dd.MM.yyyy}: {added} ta yozuv");
        }
        db.SaveChanges();
        tx.Commit();
        return new AttendanceSaveResult(added, changed, unchanged);
    }

    /// <summary>Davomat yozuvini o'chirish — faqat administrator, sabab bilan.</summary>
    public void DeleteRecord(UserSession session, int recordId, string reason)
    {
        if (session.Role != UserRole.Administrator) throw new AccessDeniedException("Davomat yozuvini faqat administrator o'chira oladi.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("O'chirish sababini kiriting.");
        using var db = _factory.Create();
        var r = db.AttendanceRecords.Find(recordId) ?? throw new BusinessRuleException("Yozuv topilmadi.");
        db.AttendanceRecords.Remove(r);
        db.Audit(session, "Davomat yozuvi o'chirildi", nameof(AttendanceRecord), recordId,
            $"o'quvchi={r.StudentId}, dars={r.LessonOccurrenceId}, holat={r.Status.ToUz()}", reason);
        db.SaveChanges();
    }

    public List<AttendanceRecord> StudentHistory(UserSession session, int studentId)
    {
        using var db = _factory.Create();
        var s = db.Students.AsNoTracking().FirstOrDefault(x => x.Id == studentId) ?? throw new BusinessRuleException("O'quvchi topilmadi.");
        if (session.Role == UserRole.Observer &&
            !db.ObserverGroupAccess.Any(a => a.UserId == session.UserId && a.GroupId == s.GroupId))
            throw new AccessDeniedException("Bu guruhni ko'rishga ruxsat yo'q.");
        return db.AttendanceRecords.AsNoTracking().Include(a => a.Lesson!).ThenInclude(l => l.Subject)
                 .Where(a => a.StudentId == studentId).ToList()
                 .OrderByDescending(a => a.Lesson!.Date).ThenByDescending(a => a.Lesson!.LessonNumber).ToList();
    }
}
