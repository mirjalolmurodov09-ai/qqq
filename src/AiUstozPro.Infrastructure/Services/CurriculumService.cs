using AiUstozPro.Application.Curriculum;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed record TopicRow(CurriculumTopic Topic, IReadOnlyList<DateOnly> LessonDates);

public sealed record PlacementPreview(PlacementResult Result, List<PlanChange> Changes, int CancelledLessons);

/// <summary>Kalendar-tematik reja (KTR).</summary>
public sealed class CurriculumService
{
    private readonly IDbFactory _factory;
    public CurriculumService(IDbFactory factory) => _factory = factory;

    public CurriculumPlan GetOrCreatePlan(UserSession session, int calendarId, int groupId, int subjectId)
    {
        using var db = _factory.Create();
        var plan = db.CurriculumPlans.AsNoTracking().FirstOrDefault(p => p.AcademicCalendarId == calendarId && p.GroupId == groupId && p.SubjectId == subjectId);
        if (plan is not null) return plan;
        session.Demand(Permission.EditCurriculum);
        var group = db.Groups.Find(groupId) ?? throw new BusinessRuleException("Guruh topilmadi.");
        var subject = db.Subjects.Find(subjectId) ?? throw new BusinessRuleException("Fan topilmadi.");
        plan = new CurriculumPlan
        {
            AcademicCalendarId = calendarId, GroupId = groupId, SubjectId = subjectId,
            Title = $"{subject.Name} — {group.Name}",
        };
        db.CurriculumPlans.Add(plan);
        db.SaveChanges();
        db.Audit(session, "KTR yaratildi", nameof(CurriculumPlan), plan.Id, plan.Title);
        db.SaveChanges();
        return plan;
    }

    public CurriculumPlan? FindPlan(int calendarId, int groupId, int subjectId)
    {
        using var db = _factory.Create();
        return db.CurriculumPlans.AsNoTracking().FirstOrDefault(p => p.AcademicCalendarId == calendarId && p.GroupId == groupId && p.SubjectId == subjectId);
    }

    public void SetPlacementMode(UserSession session, int planId, TopicPlacementMode mode)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        var p = db.CurriculumPlans.Find(planId) ?? throw new BusinessRuleException("KTR topilmadi.");
        p.PlacementMode = mode;
        db.Audit(session, "KTR joylashtirish qoidasi o'zgartirildi", nameof(CurriculumPlan), planId, mode.ToString());
        db.SaveChanges();
    }

    public List<TopicRow> ListTopics(int planId)
    {
        using var db = _factory.Create();
        var topics = db.CurriculumTopics.AsNoTracking().Where(t => t.CurriculumPlanId == planId)
                       .OrderBy(t => t.OrderNo).ThenBy(t => t.Id).ToList();
        var ids = topics.Select(t => t.Id).ToList();
        var assignments = db.TopicAssignments.AsNoTracking().Include(a => a.Lesson)
            .Where(a => ids.Contains(a.CurriculumTopicId)).ToList();
        return topics.Select(t => new TopicRow(t,
            assignments.Where(a => a.CurriculumTopicId == t.Id).Select(a => a.Lesson!.Date).OrderBy(d => d).ToList())).ToList();
    }

    public CurriculumTopic SaveTopic(UserSession session, CurriculumTopic input)
    {
        session.Demand(Permission.EditCurriculum);
        if (string.IsNullOrWhiteSpace(input.Title)) throw new BusinessRuleException("Mavzu nomini kiriting.");
        if (input.Hours < 1 || input.Hours > 100) throw new BusinessRuleException("Soat 1 dan 100 gacha bo'lishi kerak.");
        using var db = _factory.Create();
        if (!db.CurriculumPlans.Any(p => p.Id == input.CurriculumPlanId)) throw new BusinessRuleException("KTR topilmadi.");
        CurriculumTopic t;
        if (input.Id == 0)
        {
            t = new CurriculumTopic { CurriculumPlanId = input.CurriculumPlanId };
            if (input.OrderNo <= 0)
                input.OrderNo = (db.CurriculumTopics.Where(x => x.CurriculumPlanId == input.CurriculumPlanId).Max(x => (int?)x.OrderNo) ?? 0) + 1;
            db.CurriculumTopics.Add(t);
        }
        else t = db.CurriculumTopics.Find(input.Id) ?? throw new BusinessRuleException("Mavzu topilmadi.");
        t.OrderNo = input.OrderNo; t.Title = input.Title.Trim(); t.Hours = input.Hours; t.Type = input.Type;
        t.ExpectedOutcome = input.ExpectedOutcome; t.Note = input.Note; t.TeacherNote = input.TeacherNote;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Mavzu qo'shildi" : "Mavzu o'zgartirildi", nameof(CurriculumTopic), t.Id, $"{t.OrderNo}. {t.Title}");
        db.SaveChanges();
        return t;
    }

    public void DeleteTopic(UserSession session, int topicId)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        var t = db.CurriculumTopics.Find(topicId) ?? throw new BusinessRuleException("Mavzu topilmadi.");
        if (t.Status == TopicStatus.Completed || t.IsLocked)
            throw new BusinessRuleException("O'tilgan (qulflangan) mavzuni o'chirib bo'lmaydi. Avval qulfni oching.");
        db.CurriculumTopics.Remove(t);
        db.Audit(session, "Mavzu o'chirildi", nameof(CurriculumTopic), topicId, $"{t.OrderNo}. {t.Title}");
        db.SaveChanges();
    }

    /// <summary>
    /// Import qilingan mavzularni qo'shadi. replace=true — mavjud qulflanmagan mavzular almashtiriladi
    /// (o'tilgan mavzular hech qachon o'chirilmaydi).
    /// </summary>
    public int ImportTopics(UserSession session, int planId, IEnumerable<CurriculumTopic> topics, bool replace)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        if (!db.CurriculumPlans.Any(p => p.Id == planId)) throw new BusinessRuleException("KTR topilmadi.");
        var current = db.CurriculumTopics.Where(t => t.CurriculumPlanId == planId).ToList();
        if (replace)
        {
            var removable = current.Where(t => !t.IsLocked && t.Status != TopicStatus.Completed).ToList();
            db.CurriculumTopics.RemoveRange(removable);
            current = current.Except(removable).ToList();
        }
        int offset = replace ? 0 : (current.Count == 0 ? 0 : current.Max(t => t.OrderNo));
        int n = 0;
        foreach (var t in topics.OrderBy(t => t.OrderNo))
        {
            db.CurriculumTopics.Add(new CurriculumTopic
            {
                CurriculumPlanId = planId,
                OrderNo = t.OrderNo + offset,
                Title = t.Title, Hours = t.Hours, Type = t.Type,
                ExpectedOutcome = t.ExpectedOutcome, Note = t.Note,
                Status = TopicStatus.Planned,
            });
            n++;
        }
        db.Audit(session, "KTR import qilindi", nameof(CurriculumPlan), planId, $"{n} ta mavzu, {(replace ? "almashtirish" : "qo'shish")}");
        db.SaveChanges();
        tx.Commit();
        return n;
    }

    private static (List<LessonSlot> Lessons, List<TopicSlot> Topics, Dictionary<int, DateOnly?> OldDates, TopicPlacementMode Mode, int Cancelled)
        LoadPlacementInput(AppDbContext db, int planId)
    {
        var plan = db.CurriculumPlans.AsNoTracking().FirstOrDefault(p => p.Id == planId) ?? throw new BusinessRuleException("KTR topilmadi.");
        var cal = db.AcademicCalendars.AsNoTracking().First(c => c.Id == plan.AcademicCalendarId);
        var lessons = db.LessonOccurrences.AsNoTracking()
            .Where(l => l.GroupId == plan.GroupId && l.SubjectId == plan.SubjectId && l.Date >= cal.StartDate && l.Date <= cal.EndDate)
            .ToList();
        var topics = db.CurriculumTopics.AsNoTracking().Where(t => t.CurriculumPlanId == planId).ToList();
        var topicIds = topics.Select(t => t.Id).ToList();
        var assignments = db.TopicAssignments.AsNoTracking().Where(a => topicIds.Contains(a.CurriculumTopicId)).ToList();

        var lessonSlots = lessons.Select(l => new LessonSlot(l.Id, l.Date, l.LessonNumber, l.AcademicHours,
            l.Status is LessonStatus.Planned or LessonStatus.Conducted)).ToList();
        var topicSlots = topics.Select(t => new TopicSlot(t.Id, t.OrderNo, t.Title, t.Hours,
            t.IsLocked || t.Status == TopicStatus.Completed,
            assignments.Where(a => a.CurriculumTopicId == t.Id).Select(a => a.LessonOccurrenceId).ToList())).ToList();
        var oldDates = topics.ToDictionary(t => t.Id, t => t.PlannedDate);
        return (lessonSlots, topicSlots, oldDates, plan.PlacementMode, lessons.Count(l => l.Status == LessonStatus.Cancelled));
    }

    public PlacementPreview PreviewPlacement(int planId)
    {
        using var db = _factory.Create();
        var input = LoadPlacementInput(db, planId);
        var result = TopicPlacer.Place(input.Lessons, input.Topics, input.Mode);
        return new PlacementPreview(result, TopicPlacer.Diff(input.OldDates, result), input.Cancelled);
    }

    public PlacementPreview ApplyPlacement(UserSession session, int planId)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        var input = LoadPlacementInput(db, planId);
        var result = TopicPlacer.Place(input.Lessons, input.Topics, input.Mode);
        var changes = TopicPlacer.Diff(input.OldDates, result);

        var unlockedIds = result.Placements.Where(p => !p.Topic.IsLocked).Select(p => p.Topic.Id).ToList();
        db.TopicAssignments.RemoveRange(db.TopicAssignments.Where(a => unlockedIds.Contains(a.CurriculumTopicId)));
        var topics = db.CurriculumTopics.Where(t => t.CurriculumPlanId == planId).ToDictionary(t => t.Id);
        foreach (var p in result.Placements)
        {
            var t = topics[p.Topic.Id];
            if (p.Topic.IsLocked) continue;
            foreach (var part in p.Parts)
                db.TopicAssignments.Add(new TopicAssignment { CurriculumTopicId = t.Id, LessonOccurrenceId = part.LessonId, Hours = part.Hours });
            t.PlannedDate = p.PlannedDate;
            t.Status = p.IsFullyPlaced ? TopicStatus.Planned : TopicStatus.NotPlaced;
        }
        db.Audit(session, "KTR mavzulari sanalarga joylashtirildi", nameof(CurriculumPlan), planId,
            $"{result.Placements.Count} mavzu, o'zgargan sanalar: {changes.Count}, joylashmagan: {result.NotPlaced.Count()}");
        db.SaveChanges();
        tx.Commit();
        return new PlacementPreview(result, changes, input.Cancelled);
    }

    /// <summary>Mavzuni o'tildi deb belgilash: qulflanadi, qayta joylashtirishda o'z darslarida qoladi.</summary>
    public void MarkTopicCompleted(UserSession session, int topicId, DateOnly? actualDate, string? teacherNote)
    {
        session.Demand(Permission.EditCurriculum);
        using var db = _factory.Create();
        var t = db.CurriculumTopics.Find(topicId) ?? throw new BusinessRuleException("Mavzu topilmadi.");
        var lastLessonDate = db.TopicAssignments.Where(a => a.CurriculumTopicId == topicId).Select(a => a.Lesson!.Date).ToList()
                               .DefaultIfEmpty().Max();
        t.Status = TopicStatus.Completed;
        t.IsLocked = true;
        t.ActualDate = actualDate ?? (lastLessonDate == default ? DateOnly.FromDateTime(DateTime.Today) : lastLessonDate);
        if (!string.IsNullOrWhiteSpace(teacherNote)) t.TeacherNote = teacherNote.Trim();
        db.Audit(session, "Mavzu o'tildi deb belgilandi", nameof(CurriculumTopic), topicId, $"{t.OrderNo}. {t.Title}, {t.ActualDate:dd.MM.yyyy}");
        db.SaveChanges();
    }

    public void UnlockTopic(UserSession session, int topicId, string reason)
    {
        session.Demand(Permission.EditCurriculum);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Sababini kiriting.");
        using var db = _factory.Create();
        var t = db.CurriculumTopics.Find(topicId) ?? throw new BusinessRuleException("Mavzu topilmadi.");
        t.IsLocked = false;
        t.Status = TopicStatus.Planned;
        t.ActualDate = null;
        db.Audit(session, "Mavzu qulfi ochildi", nameof(CurriculumTopic), topicId, $"{t.OrderNo}. {t.Title}", reason);
        db.SaveChanges();
    }

    /// <summary>Mavzu tugallanmadi — unga qo'shimcha soat qo'shiladi; keyingi mavzular qayta joylashtirishda suriladi.</summary>
    public void ExtendTopic(UserSession session, int topicId, int extraHours, string reason)
    {
        session.Demand(Permission.EditCurriculum);
        if (extraHours < 1 || extraHours > 20) throw new BusinessRuleException("Qo'shimcha soat 1 dan 20 gacha.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Sababini kiriting.");
        using var db = _factory.Create();
        var t = db.CurriculumTopics.Find(topicId) ?? throw new BusinessRuleException("Mavzu topilmadi.");
        if (t.IsLocked) throw new BusinessRuleException("Qulflangan mavzuni uzaytirib bo'lmaydi.");
        t.Hours += extraHours;
        t.Note = string.IsNullOrWhiteSpace(t.Note) ? $"+{extraHours} soat: {reason}" : $"{t.Note}; +{extraHours} soat: {reason}";
        db.Audit(session, "Mavzu keyingi darsga davom ettirildi", nameof(CurriculumTopic), topicId, $"+{extraHours} soat", reason);
        db.SaveChanges();
    }
}
