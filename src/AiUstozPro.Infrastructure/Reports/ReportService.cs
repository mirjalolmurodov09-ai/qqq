using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Reports;

/// <summary>Formatdan mustaqil hisobot jadvali.</summary>
public sealed class ReportTable
{
    public string Title { get; set; } = "";
    public List<string> SubtitleLines { get; } = new();
    public List<string> Columns { get; } = new();
    /// <summary>Ustunlarning nisbiy kengligi (PDF uchun). Bo'sh bo'lsa hammasi 1.</summary>
    public List<float> Widths { get; } = new();
    public List<string[]> Rows { get; } = new();
    public List<string> FooterLines { get; } = new();
    public bool Landscape { get; set; }
}

public sealed class ReportService
{
    public const string DateFormat = "dd.MM.yyyy";
    private readonly IDbFactory _factory;
    public ReportService(IDbFactory factory) => _factory = factory;

    private static void DemandGroup(AppDbContext db, UserSession session, int groupId)
    {
        session.Demand(Permission.ViewReports);
        if (session.Role == UserRole.Observer && !db.ObserverGroupAccess.Any(a => a.UserId == session.UserId && a.GroupId == groupId))
            throw new AccessDeniedException("Bu guruh hisobotlarini ko'rishga ruxsat yo'q.");
    }

    /// <summary>Davomat jurnali: o'quvchilar × dars sanalari, yakuniy statistika bilan.</summary>
    public ReportTable AttendanceMatrix(UserSession session, int groupId, int? subjectId, DateOnly from, DateOnly to)
    {
        if (to < from) throw new BusinessRuleException("Davr noto'g'ri tanlangan.");
        using var db = _factory.Create();
        DemandGroup(db, session, groupId);
        var group = db.Groups.AsNoTracking().First(g => g.Id == groupId);
        var lessonsQ = db.LessonOccurrences.AsNoTracking().Include(l => l.Subject)
            .Where(l => l.GroupId == groupId && l.Date >= from && l.Date <= to && l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Skipped);
        if (subjectId is int sid) lessonsQ = lessonsQ.Where(l => l.SubjectId == sid);
        var lessons = lessonsQ.OrderBy(l => l.Date).ThenBy(l => l.LessonNumber).ToList();
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var records = db.AttendanceRecords.AsNoTracking().Where(a => lessonIds.Contains(a.LessonOccurrenceId)).ToList();
        var recordStudentIds = records.Select(r => r.StudentId).Distinct().ToList();
        var students = db.Students.AsNoTracking()
            .Where(s => s.GroupId == groupId && (s.IsActive || recordStudentIds.Contains(s.Id)))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();

        var t = new ReportTable { Title = "Davomat jurnali", Landscape = true };
        t.SubtitleLines.Add($"Guruh: {group.Name}");
        if (subjectId is not null && lessons.Count > 0) t.SubtitleLines.Add($"Fan: {lessons[0].Subject?.Name}");
        t.SubtitleLines.Add($"Davr: {from.ToString(DateFormat)} — {to.ToString(DateFormat)}");
        t.Columns.Add("№"); t.Widths.Add(0.6f);
        t.Columns.Add("F.I.Sh."); t.Widths.Add(5f);
        foreach (var l in lessons)
        {
            t.Columns.Add(subjectId is null ? $"{l.Date:dd.MM} {Abbrev(l.Subject?.Name)}" : $"{l.Date:dd.MM}");
            t.Widths.Add(1f);
        }
        foreach (var h in new[] { "Keldi", "Kech.", "Sababli", "Sababsiz", "Davomat %" }) { t.Columns.Add(h); t.Widths.Add(1.3f); }

        var byKey = records.ToDictionary(r => (r.StudentId, r.LessonOccurrenceId));
        int i = 0;
        int totPresent = 0, totMarked = 0;
        foreach (var s in students)
        {
            var row = new List<string> { (++i).ToString(), s.FullName + (s.IsActive ? "" : " (arxiv)") };
            int present = 0, late = 0, exc = 0, unexc = 0, marked = 0;
            foreach (var l in lessons)
            {
                if (byKey.TryGetValue((s.Id, l.Id), out var r))
                {
                    row.Add(r.Status.ToShortUz());
                    marked++;
                    switch (r.Status)
                    {
                        case AttendanceStatus.Present: present++; break;
                        case AttendanceStatus.Late: late++; break;
                        case AttendanceStatus.AbsentExcused: exc++; break;
                        case AttendanceStatus.AbsentUnexcused: unexc++; break;
                        case AttendanceStatus.LeftWithPermission: present++; break;
                    }
                }
                else row.Add("");
            }
            var attended = present + late;
            row.Add(present.ToString()); row.Add(late.ToString()); row.Add(exc.ToString()); row.Add(unexc.ToString());
            row.Add(marked == 0 ? "—" : $"{Math.Round(100.0 * attended / marked, 1):0.#}%");
            totPresent += attended; totMarked += marked;
            t.Rows.Add(row.ToArray());
        }
        t.FooterLines.Add("Belgilar: + keldi, K kechikdi, S sababli kelmadi, Y sababsiz kelmadi, R ruxsat bilan chiqdi, ? aniqlanmadi.");
        t.FooterLines.Add($"Darslar soni: {lessons.Count}. Umumiy davomat: {(totMarked == 0 ? "—" : $"{Math.Round(100.0 * totPresent / totMarked, 1):0.#}%")}.");
        t.FooterLines.Add($"Tayyorlandi: {DateTime.Now:dd.MM.yyyy HH:mm}, {session.FullName}");
        return t;
    }

    /// <summary>AI tahlili uchun ANONIM davomat ma'lumoti (ismlarsiz, tartibi aralashtirilgan).</summary>
    public string AnonymousAttendance(UserSession session, int groupId, int? subjectId, DateOnly from, DateOnly to)
    {
        using var db = _factory.Create();
        DemandGroup(db, session, groupId);
        var group = db.Groups.AsNoTracking().First(g => g.Id == groupId);
        var q = db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Lesson!.GroupId == groupId && a.Lesson.Date >= from && a.Lesson.Date <= to);
        if (subjectId is int sid) q = q.Where(a => a.Lesson!.SubjectId == sid);
        var recs = q.Select(a => new { a.StudentId, a.Status }).ToList();
        var stats = recs.GroupBy(r => r.StudentId).Select(g => new Anonymizer.StudentStat("",
            g.Count(x => x.Status is AttendanceStatus.Present or AttendanceStatus.LeftWithPermission),
            g.Count(x => x.Status == AttendanceStatus.Late),
            g.Count(x => x.Status == AttendanceStatus.AbsentExcused),
            g.Count(x => x.Status == AttendanceStatus.AbsentUnexcused),
            g.Count())).ToList();
        var label = string.IsNullOrWhiteSpace(group.Course) ? "tanlangan guruh" : group.Course!;
        return Anonymizer.AttendanceSummary(label, $"{from.ToString(DateFormat)} — {to.ToString(DateFormat)}", stats);
    }

    /// <summary>Bitta o'quvchining davomat tarixi.</summary>
    public ReportTable StudentHistory(UserSession session, int studentId)
    {
        using var db = _factory.Create();
        var s = db.Students.AsNoTracking().Include(x => x.Group).FirstOrDefault(x => x.Id == studentId) ?? throw new BusinessRuleException("O'quvchi topilmadi.");
        DemandGroup(db, session, s.GroupId);
        var recs = db.AttendanceRecords.AsNoTracking().Include(a => a.Lesson!).ThenInclude(l => l.Subject)
            .Where(a => a.StudentId == studentId).ToList()
            .OrderBy(a => a.Lesson!.Date).ThenBy(a => a.Lesson!.LessonNumber).ToList();
        var t = new ReportTable { Title = "O'quvchi davomati tarixi" };
        t.SubtitleLines.Add($"O'quvchi: {s.FullName}");
        t.SubtitleLines.Add($"Guruh: {s.Group?.Name}");
        t.Columns.AddRange(new[] { "Sana", "Dars", "Fan", "Holat", "Izoh" });
        t.Widths.AddRange(new[] { 1.4f, 0.8f, 3f, 2.4f, 4f });
        foreach (var r in recs)
            t.Rows.Add(new[] { r.Lesson!.Date.ToString(DateFormat), r.Lesson.LessonNumber.ToString(), r.Lesson.Subject?.Name ?? "", r.Status.ToUz(), r.Note ?? "" });
        t.FooterLines.Add($"Jami yozuvlar: {recs.Count}; sababsiz: {recs.Count(r => r.Status == AttendanceStatus.AbsentUnexcused)}; sababli: {recs.Count(r => r.Status == AttendanceStatus.AbsentExcused)}.");
        return t;
    }

    /// <summary>KTR: reja va amalda o'tilganlar taqqoslanishi, oylar kesimida.</summary>
    public ReportTable Curriculum(UserSession session, int planId, DateOnly today)
    {
        using var db = _factory.Create();
        var plan = db.CurriculumPlans.AsNoTracking().Include(p => p.Group).Include(p => p.Subject).FirstOrDefault(p => p.Id == planId)
                   ?? throw new BusinessRuleException("KTR topilmadi.");
        DemandGroup(db, session, plan.GroupId);
        var topics = db.CurriculumTopics.AsNoTracking().Where(t => t.CurriculumPlanId == planId).OrderBy(t => t.OrderNo).ToList();
        var ids = topics.Select(t => t.Id).ToList();
        var asg = db.TopicAssignments.AsNoTracking().Include(a => a.Lesson).Where(a => ids.Contains(a.CurriculumTopicId)).ToList();

        var t = new ReportTable { Title = "Kalendar-tematik reja", Landscape = true };
        t.SubtitleLines.Add($"Fan: {plan.Subject?.Name}");
        t.SubtitleLines.Add($"Guruh: {plan.Group?.Name}");
        t.Columns.AddRange(new[] { "№", "Mavzu", "Soat", "Turi", "Oy", "Rejadagi sana(lar)", "O'tilgan sana", "Holat", "Izoh" });
        t.Widths.AddRange(new[] { 0.6f, 6f, 0.8f, 1.4f, 1.3f, 2.6f, 1.6f, 1.8f, 3f });
        int totalHours = 0, doneHours = 0, behind = 0;
        foreach (var topic in topics)
        {
            var dates = asg.Where(a => a.CurriculumTopicId == topic.Id).Select(a => a.Lesson!.Date).OrderBy(d => d).ToList();
            totalHours += topic.Hours;
            var done = topic.Status == TopicStatus.Completed;
            if (done) doneHours += topic.Hours;
            var isBehind = !done && dates.Count > 0 && dates.Max() < today;
            if (isBehind) behind++;
            var status = isBehind ? "Rejadan ortda" : topic.Status.ToUz();
            var month = (topic.PlannedDate ?? (dates.Count > 0 ? dates[0] : (DateOnly?)null)) is { } pd ? MonthUz(pd.Month) : "";
            t.Rows.Add(new[]
            {
                topic.OrderNo.ToString(), topic.Title, topic.Hours.ToString(), topic.Type.ToUz(), month,
                string.Join(", ", dates.Select(d => d.ToString(DateFormat))),
                topic.ActualDate?.ToString(DateFormat) ?? "", status,
                string.Join("; ", new[] { topic.Note, topic.TeacherNote }.Where(x => !string.IsNullOrWhiteSpace(x))),
            });
        }
        t.FooterLines.Add($"Jami: {topics.Count} mavzu, {totalHours} soat. O'tildi: {doneHours} soat. Qolgan: {totalHours - doneHours} soat. Rejadan ortda: {behind} mavzu.");
        var notPlaced = topics.Count(x => x.Status == TopicStatus.NotPlaced);
        if (notPlaced > 0) t.FooterLines.Add($"Diqqat: {notPlaced} ta mavzuga dars sanasi yetmadi.");
        t.FooterLines.Add($"Tayyorlandi: {DateTime.Now:dd.MM.yyyy HH:mm}, {session.FullName}");
        return t;
    }

    /// <summary>Davr bo'yicha darslar statistikasi (guruh × fan).</summary>
    public ReportTable LessonStatistics(UserSession session, DateOnly from, DateOnly to)
    {
        session.Demand(Permission.ViewReports);
        using var db = _factory.Create();
        var q = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject)
                  .Where(l => l.Date >= from && l.Date <= to);
        if (session.Role == UserRole.Observer)
        {
            var allowed = db.ObserverGroupAccess.Where(a => a.UserId == session.UserId).Select(a => a.GroupId).ToList();
            q = q.Where(l => allowed.Contains(l.GroupId));
        }
        var lessons = q.ToList();
        var t = new ReportTable { Title = "Darslar statistikasi" };
        t.SubtitleLines.Add($"Davr: {from.ToString(DateFormat)} — {to.ToString(DateFormat)}");
        t.Columns.AddRange(new[] { "Guruh", "Fan", "Rejada", "O'tildi", "Bekor qilindi", "Qo'shimcha", "Ko'chirilgan", "Soat (o'tilgan)" });
        t.Widths.AddRange(new[] { 2f, 3f, 1f, 1f, 1.3f, 1.2f, 1.3f, 1.4f });
        foreach (var g in lessons.GroupBy(l => (l.Group!.Name, l.Subject!.Name)).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
        {
            t.Rows.Add(new[]
            {
                g.Key.Item1, g.Key.Item2,
                g.Count(l => l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Skipped).ToString(),
                g.Count(l => l.Status == LessonStatus.Conducted).ToString(),
                g.Count(l => l.Status == LessonStatus.Cancelled).ToString(),
                g.Count(l => l.Origin == LessonOrigin.Manual).ToString(),
                g.Count(l => l.Origin == LessonOrigin.Moved).ToString(),
                g.Where(l => l.Status == LessonStatus.Conducted).Sum(l => l.AcademicHours).ToString(),
            });
        }
        t.FooterLines.Add($"Tayyorlandi: {DateTime.Now:dd.MM.yyyy HH:mm}, {session.FullName}");
        return t;
    }

    /// <summary>Audit jurnali (faqat administrator).</summary>
    public ReportTable AuditLog(UserSession session, DateOnly from, DateOnly to)
    {
        session.Demand(Permission.ViewAuditLog);
        using var db = _factory.Create();
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
        var logs = db.AuditLogs.AsNoTracking().Where(a => a.TimestampUtc >= fromUtc && a.TimestampUtc < toUtc)
                     .OrderByDescending(a => a.TimestampUtc).Take(5000).ToList();
        var t = new ReportTable { Title = "Audit jurnali", Landscape = true };
        t.SubtitleLines.Add($"Davr: {from.ToString(DateFormat)} — {to.ToString(DateFormat)}");
        t.Columns.AddRange(new[] { "Vaqt", "Foydalanuvchi", "Amal", "Obyekt", "Tafsilot", "Sabab" });
        t.Widths.AddRange(new[] { 2f, 1.5f, 3f, 1.8f, 5f, 3f });
        foreach (var a in logs)
            t.Rows.Add(new[]
            {
                a.TimestampUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), a.UserLogin, a.Action,
                a.EntityId is null ? a.EntityType : $"{a.EntityType} #{a.EntityId}", a.Details ?? "", a.Reason ?? "",
            });
        return t;
    }

    public static string MonthUz(int m) => m switch
    {
        1 => "Yanvar", 2 => "Fevral", 3 => "Mart", 4 => "Aprel", 5 => "May", 6 => "Iyun",
        7 => "Iyul", 8 => "Avgust", 9 => "Sentabr", 10 => "Oktabr", 11 => "Noyabr", 12 => "Dekabr", _ => "",
    };

    private static string Abbrev(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Length <= 4 ? s : s[..4] + ".";
    }
}
