using AiUstozPro.Application.Calendar;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed record LessonPreview(IReadOnlyList<GeneratedLesson> AllDates, ReconcileResult Reconcile);

/// <summary>O'quv kalendari, istisnolar, dars jadvali va dars sanalari.</summary>
public sealed class CalendarService
{
    private readonly IDbFactory _factory;
    public CalendarService(IDbFactory factory) => _factory = factory;

    // ---------- Kalendar ----------

    public AcademicCalendar? GetActiveCalendar()
    {
        using var db = _factory.Create();
        return db.AcademicCalendars.AsNoTracking().Include(c => c.Terms)
                 .Where(c => c.IsActive).OrderByDescending(c => c.StartDate).FirstOrDefault();
    }

    public List<AcademicCalendar> ListCalendars()
    {
        using var db = _factory.Create();
        return db.AcademicCalendars.AsNoTracking().OrderByDescending(c => c.StartDate).ToList();
    }

    public AcademicCalendar SaveCalendar(UserSession session, AcademicCalendar input)
    {
        session.Demand(Permission.EditCalendar);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new BusinessRuleException("O'quv yili nomini kiriting (masalan, 2026–2027).");
        if (input.EndDate <= input.StartDate) throw new BusinessRuleException("Tugash sanasi boshlanish sanasidan keyin bo'lishi kerak.");
        if (input.EndDate.DayNumber - input.StartDate.DayNumber > 400) throw new BusinessRuleException("O'quv yili 400 kundan uzun bo'lishi mumkin emas.");
        if ((input.WorkingDaysMask & 0b1111111) == 0) throw new BusinessRuleException("Kamida bitta ish kunini belgilang.");
        using var db = _factory.Create();
        AcademicCalendar c;
        if (input.Id == 0) { c = new AcademicCalendar(); db.AcademicCalendars.Add(c); }
        else c = db.AcademicCalendars.Find(input.Id) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        c.Name = input.Name.Trim();
        c.StartDate = input.StartDate;
        c.EndDate = input.EndDate;
        c.WorkingDaysMask = input.WorkingDaysMask;
        c.IsActive = true;
        foreach (var other in db.AcademicCalendars.Where(x => x.Id != c.Id && x.IsActive)) other.IsActive = false;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "O'quv yili yaratildi" : "O'quv yili o'zgartirildi", nameof(AcademicCalendar), c.Id,
            $"{c.Name}: {c.StartDate:dd.MM.yyyy}–{c.EndDate:dd.MM.yyyy}");
        db.SaveChanges();
        return c;
    }

    public List<AcademicTerm> ListTerms(int calendarId)
    {
        using var db = _factory.Create();
        return db.AcademicTerms.AsNoTracking().Where(t => t.AcademicCalendarId == calendarId).OrderBy(t => t.StartDate).ToList();
    }

    public void SaveTerm(UserSession session, AcademicTerm input)
    {
        session.Demand(Permission.EditCalendar);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new BusinessRuleException("Davr nomini kiriting.");
        if (input.EndDate < input.StartDate) throw new BusinessRuleException("Davr tugash sanasi noto'g'ri.");
        using var db = _factory.Create();
        var cal = db.AcademicCalendars.Find(input.AcademicCalendarId) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        if (input.StartDate < cal.StartDate || input.EndDate > cal.EndDate)
            throw new BusinessRuleException("Semestr/chorak o'quv yili chegarasida bo'lishi kerak.");
        AcademicTerm t;
        if (input.Id == 0) { t = new AcademicTerm { AcademicCalendarId = cal.Id }; db.AcademicTerms.Add(t); }
        else t = db.AcademicTerms.Find(input.Id) ?? throw new BusinessRuleException("Davr topilmadi.");
        t.Name = input.Name.Trim(); t.StartDate = input.StartDate; t.EndDate = input.EndDate;
        db.Audit(session, "Semestr/chorak saqlandi", nameof(AcademicTerm), input.Id == 0 ? null : input.Id, t.Name);
        db.SaveChanges();
    }

    public void DeleteTerm(UserSession session, int termId)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        var t = db.AcademicTerms.Find(termId);
        if (t is null) return;
        db.AcademicTerms.Remove(t);
        db.Audit(session, "Semestr/chorak o'chirildi", nameof(AcademicTerm), termId, t.Name);
        db.SaveChanges();
    }

    // ---------- Istisnolar (bayram, ta'til, ko'chirilgan ish kuni) ----------

    public List<CalendarException> ListExceptions(int calendarId)
    {
        using var db = _factory.Create();
        return db.CalendarExceptions.AsNoTracking().Where(x => x.AcademicCalendarId == calendarId)
                 .OrderBy(x => x.StartDate).ToList();
    }

    public CalendarException SaveException(UserSession session, CalendarException input)
    {
        session.Demand(Permission.EditCalendar);
        if (input.Kind != CalendarExceptionKind.Vacation) input.EndDate = input.StartDate;
        if (input.EndDate < input.StartDate) throw new BusinessRuleException("Ta'til tugash sanasi boshlanishdan oldin bo'lishi mumkin emas.");
        if (input.Kind == CalendarExceptionKind.TransferredWorkday && input.WorksAsDayOfWeek is null)
            throw new BusinessRuleException("Ko'chirilgan ish kuni qaysi hafta kuni jadvali bo'yicha ishlashini tanlang.");
        if (input.Kind != CalendarExceptionKind.TransferredWorkday) input.WorksAsDayOfWeek = null;
        using var db = _factory.Create();
        var cal = db.AcademicCalendars.Find(input.AcademicCalendarId) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        if (input.StartDate < cal.StartDate || input.EndDate > cal.EndDate)
            throw new BusinessRuleException($"Sana o'quv yili ichida bo'lishi kerak ({cal.StartDate:dd.MM.yyyy}–{cal.EndDate:dd.MM.yyyy}).");

        CalendarException x;
        if (input.Id == 0) { x = new CalendarException { AcademicCalendarId = cal.Id }; db.CalendarExceptions.Add(x); }
        else x = db.CalendarExceptions.Find(input.Id) ?? throw new BusinessRuleException("Istisno topilmadi.");
        x.Kind = input.Kind; x.StartDate = input.StartDate; x.EndDate = input.EndDate;
        x.WorksAsDayOfWeek = input.WorksAsDayOfWeek; x.Title = (input.Title ?? "").Trim();
        x.GroupId = input.GroupId; x.SubjectId = input.SubjectId;
        x.IsConfirmed = input.IsConfirmed; x.Source = input.Source;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Kalendar istisnosi qo'shildi" : "Kalendar istisnosi o'zgartirildi", nameof(CalendarException), x.Id,
            $"{x.Kind.ToUz()} {x.StartDate:dd.MM.yyyy}–{x.EndDate:dd.MM.yyyy} {x.Title}");
        db.SaveChanges();
        return x;
    }

    public void DeleteException(UserSession session, int id)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        var x = db.CalendarExceptions.Find(id);
        if (x is null) return;
        db.CalendarExceptions.Remove(x);
        db.Audit(session, "Kalendar istisnosi o'chirildi", nameof(CalendarException), id, $"{x.Kind.ToUz()} {x.StartDate:dd.MM.yyyy} {x.Title}");
        db.SaveChanges();
    }

    /// <summary>Sanasi qat'iy bayramlarni qo'shadi (allaqachon bor bo'lsa takrorlamaydi). Qo'shilganlar sonini qaytaradi.</summary>
    public int AddFixedHolidays(UserSession session, int calendarId)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        var cal = db.AcademicCalendars.Find(calendarId) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        var existing = db.CalendarExceptions.Where(x => x.AcademicCalendarId == calendarId && x.Kind == CalendarExceptionKind.Holiday)
                         .Select(x => x.StartDate).ToList().ToHashSet();
        int n = 0;
        foreach (var h in UzbekistanHolidays.FixedHolidaysFor(cal))
        {
            if (existing.Contains(h.StartDate)) continue;
            db.CalendarExceptions.Add(h);
            n++;
        }
        db.Audit(session, "Qat'iy bayramlar qo'shildi", nameof(AcademicCalendar), calendarId, $"{n} ta");
        db.SaveChanges();
        return n;
    }

    // ---------- Dars jadvali ----------

    public List<TimetableEntry> ListTimetable(int calendarId, int? groupId = null, bool includeInactive = false)
    {
        using var db = _factory.Create();
        var q = db.TimetableEntries.AsNoTracking().Include(e => e.Group).Include(e => e.Subject).Include(e => e.Teacher)
                  .Where(e => e.AcademicCalendarId == calendarId);
        if (groupId is int g) q = q.Where(e => e.GroupId == g);
        if (!includeInactive) q = q.Where(e => e.IsActive);
        return q.ToList().OrderBy(e => (((int)e.DayOfWeek) + 6) % 7).ThenBy(e => e.LessonNumber).ThenBy(e => e.Group!.Name).ToList();
    }

    public List<TimetableConflict> CheckConflicts(TimetableEntry candidate)
    {
        using var db = _factory.Create();
        var others = db.TimetableEntries.AsNoTracking().Where(e => e.AcademicCalendarId == candidate.AcademicCalendarId && e.IsActive).ToList();
        return TimetableConflictChecker.Check(candidate, others);
    }

    /// <summary>Jadval yozuvini saqlaydi. Ziddiyat bo'lsa va allowConflicts=false bo'lsa — saqlamaydi, ziddiyatlarni qaytaradi.</summary>
    public (TimetableEntry? Saved, List<TimetableConflict> Conflicts) SaveTimetableEntry(UserSession session, TimetableEntry input, bool allowConflicts)
    {
        session.Demand(Permission.EditTimetable);
        if (input.LessonNumber < 1 || input.LessonNumber > 12) throw new BusinessRuleException("Dars raqami 1 dan 12 gacha bo'lishi kerak.");
        if (input.AcademicHours < 1 || input.AcademicHours > 8) throw new BusinessRuleException("Akademik soat 1 dan 8 gacha bo'lishi kerak.");
        if (input.ValidFrom is { } f && input.ValidTo is { } t && t < f) throw new BusinessRuleException("Amal qilish davri noto'g'ri.");
        var conflicts = CheckConflicts(input);
        if (conflicts.Any(c => ReferenceEquals(c.Other, input))) throw new BusinessRuleException(conflicts[0].Message);
        if (conflicts.Count > 0 && !allowConflicts) return (null, conflicts);

        using var db = _factory.Create();
        if (!db.Groups.Any(g => g.Id == input.GroupId)) throw new BusinessRuleException("Guruh tanlanmagan.");
        if (!db.Subjects.Any(s => s.Id == input.SubjectId)) throw new BusinessRuleException("Fan tanlanmagan.");
        if (!db.Users.Any(u => u.Id == input.TeacherUserId)) throw new BusinessRuleException("O'qituvchi tanlanmagan.");
        TimetableEntry e;
        if (input.Id == 0) { e = new TimetableEntry(); db.TimetableEntries.Add(e); }
        else e = db.TimetableEntries.Find(input.Id) ?? throw new BusinessRuleException("Jadval yozuvi topilmadi.");
        e.AcademicCalendarId = input.AcademicCalendarId; e.GroupId = input.GroupId; e.SubjectId = input.SubjectId;
        e.TeacherUserId = input.TeacherUserId; e.DayOfWeek = input.DayOfWeek; e.LessonNumber = input.LessonNumber;
        e.StartTime = input.StartTime; e.EndTime = input.EndTime; e.Room = string.IsNullOrWhiteSpace(input.Room) ? null : input.Room.Trim();
        e.AcademicHours = input.AcademicHours; e.ValidFrom = input.ValidFrom; e.ValidTo = input.ValidTo; e.IsActive = true;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Jadvalga dars qo'shildi" : "Jadval o'zgartirildi", nameof(TimetableEntry), e.Id,
            $"{e.DayOfWeek.ToUz()} {e.LessonNumber}-dars {e.StartTime:HH\\:mm}" + (conflicts.Count > 0 ? $"; ziddiyat bilan saqlandi: {string.Join(" ", conflicts.Select(c => c.Message))}" : ""));
        db.SaveChanges();
        return (e, conflicts);
    }

    /// <summary>
    /// Jadval yozuvini o'chiradi. Agar unga bog'liq o'tilgan dars yoki davomat bo'lsa — o'chirilmaydi, faqat
    /// faolsizlantiriladi (tarixiy ma'lumot saqlanadi). Kelajakdagi himoyalanmagan avtomatik darslar o'chiriladi.
    /// </summary>
    public string DeleteTimetableEntry(UserSession session, int entryId)
    {
        session.Demand(Permission.EditTimetable);
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        var e = db.TimetableEntries.Find(entryId) ?? throw new BusinessRuleException("Jadval yozuvi topilmadi.");
        var lessons = db.LessonOccurrences.Where(l => l.TimetableEntryId == entryId).ToList();
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var withAttendance = db.AttendanceRecords.Where(a => lessonIds.Contains(a.LessonOccurrenceId)).Select(a => a.LessonOccurrenceId).Distinct().ToList().ToHashSet();
        int removed = 0;
        foreach (var l in lessons)
        {
            var isProtected = withAttendance.Contains(l.Id) || l.IsConfirmed || l.Status == LessonStatus.Conducted || l.Origin != LessonOrigin.Generated;
            if (!isProtected) { db.LessonOccurrences.Remove(l); removed++; }
        }
        string msg;
        if (lessons.Count - removed > 0)
        {
            e.IsActive = false;
            msg = $"Jadval yozuvi faolsizlantirildi. {removed} ta rejalashtirilgan dars o'chirildi, {lessons.Count - removed} ta tarixiy dars saqlab qolindi.";
        }
        else
        {
            db.TimetableEntries.Remove(e);
            msg = $"Jadval yozuvi o'chirildi ({removed} ta rejalashtirilgan dars bilan).";
        }
        db.Audit(session, "Jadval yozuvi o'chirildi", nameof(TimetableEntry), entryId, msg);
        db.SaveChanges();
        tx.Commit();
        return msg;
    }

    /// <summary>Jadvalda mavjud (guruh, fan) juftliklari.</summary>
    public List<(int GroupId, string GroupName, int SubjectId, string SubjectName)> ListGroupSubjectPairs(int calendarId)
    {
        using var db = _factory.Create();
        return db.TimetableEntries.AsNoTracking().Include(e => e.Group).Include(e => e.Subject)
            .Where(e => e.AcademicCalendarId == calendarId).ToList()
            .GroupBy(e => (e.GroupId, e.SubjectId))
            .Select(g => (g.Key.GroupId, g.First().Group!.Name, g.Key.SubjectId, g.First().Subject!.Name))
            .OrderBy(x => x.Item2).ThenBy(x => x.Item4).ToList();
    }

    // ---------- Dars sanalarini hisoblash ----------

    public LessonPreview PreviewLessons(int calendarId, int groupId, int subjectId)
    {
        using var db = _factory.Create();
        var cal = db.AcademicCalendars.AsNoTracking().FirstOrDefault(c => c.Id == calendarId) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        var exceptions = db.CalendarExceptions.AsNoTracking().Where(x => x.AcademicCalendarId == calendarId).ToList();
        var entries = db.TimetableEntries.AsNoTracking()
            .Where(e => e.AcademicCalendarId == calendarId && e.GroupId == groupId && e.SubjectId == subjectId).ToList();
        var all = LessonDateGenerator.Generate(cal, exceptions, entries, includeSkipped: true);
        var existing = LoadExisting(db, groupId, subjectId, cal);
        var rec = OccurrenceReconciler.Reconcile(existing, all);
        return new LessonPreview(all, rec);
    }

    public ReconcileResult ApplyLessons(UserSession session, int calendarId, int groupId, int subjectId)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        var cal = db.AcademicCalendars.AsNoTracking().FirstOrDefault(c => c.Id == calendarId) ?? throw new BusinessRuleException("Kalendar topilmadi.");
        var exceptions = db.CalendarExceptions.AsNoTracking().Where(x => x.AcademicCalendarId == calendarId).ToList();
        var entries = db.TimetableEntries.AsNoTracking()
            .Where(e => e.AcademicCalendarId == calendarId && e.GroupId == groupId && e.SubjectId == subjectId).ToList();
        if (entries.Count == 0) throw new BusinessRuleException("Bu guruh va fan uchun dars jadvali kiritilmagan.");
        var generated = LessonDateGenerator.Generate(cal, exceptions, entries, includeSkipped: false);
        var existing = LoadExisting(db, groupId, subjectId, cal);
        var rec = OccurrenceReconciler.Reconcile(existing, generated);

        var removeIds = rec.ToRemove.Select(r => r.Id).ToList();
        if (removeIds.Count > 0)
            db.LessonOccurrences.RemoveRange(db.LessonOccurrences.Where(l => removeIds.Contains(l.Id)));
        foreach (var g in rec.ToAdd)
        {
            db.LessonOccurrences.Add(new LessonOccurrence
            {
                GroupId = g.GroupId, SubjectId = g.SubjectId, TimetableEntryId = g.TimetableEntryId,
                TeacherUserId = g.TeacherUserId, Date = g.Date, LessonNumber = g.LessonNumber,
                StartTime = g.StartTime, EndTime = g.EndTime, Room = g.Room, AcademicHours = g.AcademicHours,
                Status = LessonStatus.Planned, Origin = LessonOrigin.Generated,
            });
        }
        db.Audit(session, "Dars sanalari hisoblandi", "LessonOccurrence", null,
            $"guruh={groupId}, fan={subjectId}: +{rec.ToAdd.Count}, -{rec.ToRemove.Count}, saqlandi={rec.Kept.Count}, ziddiyat={rec.Conflicts.Count}");
        db.SaveChanges();
        tx.Commit();
        return rec;
    }

    private static List<ExistingLesson> LoadExisting(AppDbContext db, int groupId, int subjectId, AcademicCalendar cal)
    {
        var lessons = db.LessonOccurrences.AsNoTracking()
            .Where(l => l.GroupId == groupId && l.SubjectId == subjectId && l.Date >= cal.StartDate && l.Date <= cal.EndDate)
            .ToList();
        var ids = lessons.Select(l => l.Id).ToList();
        var withAtt = db.AttendanceRecords.AsNoTracking().Where(a => ids.Contains(a.LessonOccurrenceId)).Select(a => a.LessonOccurrenceId).Distinct().ToList().ToHashSet();
        var withTopic = db.TopicAssignments.AsNoTracking().Where(a => ids.Contains(a.LessonOccurrenceId)).Select(a => a.LessonOccurrenceId).Distinct().ToList().ToHashSet();
        return lessons.Select(l => new ExistingLesson(l.Id, l.TimetableEntryId, l.Date, l.OriginalDate, l.Origin, l.Status,
            l.IsConfirmed, withAtt.Contains(l.Id), withTopic.Contains(l.Id))).ToList();
    }

    // ---------- Darslar ro'yxati va qo'lda tahrirlash ----------

    public List<LessonOccurrence> ListLessons(int groupId, int subjectId, DateOnly? from = null, DateOnly? to = null)
    {
        using var db = _factory.Create();
        var q = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject)
                  .Where(l => l.GroupId == groupId && l.SubjectId == subjectId);
        if (from is { } f) q = q.Where(l => l.Date >= f);
        if (to is { } t) q = q.Where(l => l.Date <= t);
        return q.OrderBy(l => l.Date).ThenBy(l => l.LessonNumber).ToList();
    }

    public List<LessonOccurrence> LessonsOnDate(UserSession session, DateOnly date)
    {
        using var db = _factory.Create();
        var q = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject)
                  .Where(l => l.Date == date && l.Status != LessonStatus.Skipped);
        if (session.Role == UserRole.Teacher) q = q.Where(l => l.TeacherUserId == session.UserId);
        if (session.Role == UserRole.Observer)
        {
            var allowed = db.ObserverGroupAccess.Where(a => a.UserId == session.UserId).Select(a => a.GroupId).ToList();
            q = q.Where(l => allowed.Contains(l.GroupId));
        }
        return q.OrderBy(l => l.StartTime).ThenBy(l => l.LessonNumber).ToList();
    }

    public LessonOccurrence? NextLesson(UserSession session, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        using var db = _factory.Create();
        var q = db.LessonOccurrences.AsNoTracking().Include(l => l.Group).Include(l => l.Subject)
                  .Where(l => l.Status == LessonStatus.Planned && l.Date >= today);
        if (session.Role == UserRole.Teacher) q = q.Where(l => l.TeacherUserId == session.UserId);
        return q.OrderBy(l => l.Date).ThenBy(l => l.StartTime).Take(50).ToList()
                .FirstOrDefault(l => l.Date > today || l.StartTime >= time);
    }

    private static LessonOccurrence Load(AppDbContext db, int lessonId)
        => db.LessonOccurrences.Find(lessonId) ?? throw new BusinessRuleException("Dars topilmadi.");

    public void CancelLesson(UserSession session, int lessonId, string reason)
    {
        session.Demand(Permission.EditCalendar);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Bekor qilish sababini kiriting.");
        using var db = _factory.Create();
        var l = Load(db, lessonId);
        if (db.AttendanceRecords.Any(a => a.LessonOccurrenceId == lessonId))
            throw new BusinessRuleException("Bu darsda davomat olingan. Avval davomatni ko'rib chiqing — bekor qilinmadi.");
        l.Status = LessonStatus.Cancelled;
        l.Note = reason.Trim();
        db.Audit(session, "Dars bekor qilindi", nameof(LessonOccurrence), l.Id, $"{l.Date:dd.MM.yyyy}", reason);
        db.SaveChanges();
    }

    public void RestoreLesson(UserSession session, int lessonId)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        var l = Load(db, lessonId);
        l.Status = LessonStatus.Planned;
        db.Audit(session, "Dars tiklandi", nameof(LessonOccurrence), l.Id, $"{l.Date:dd.MM.yyyy}");
        db.SaveChanges();
    }

    public void MoveLesson(UserSession session, int lessonId, DateOnly newDate, int? newLessonNumber, TimeOnly? newStart, TimeOnly? newEnd, string reason)
    {
        session.Demand(Permission.EditCalendar);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Ko'chirish sababini kiriting.");
        using var db = _factory.Create();
        var l = Load(db, lessonId);
        if (db.AttendanceRecords.Any(a => a.LessonOccurrenceId == lessonId))
            throw new BusinessRuleException("Bu darsda davomat olingan — sanani o'zgartirib bo'lmaydi.");
        var old = l.Date;
        if (l.Origin == LessonOrigin.Generated) { l.OriginalDate = l.Date; l.Origin = LessonOrigin.Moved; }
        l.Date = newDate;
        if (newLessonNumber is int n) l.LessonNumber = n;
        if (newStart is { } s) l.StartTime = s;
        if (newEnd is { } e) l.EndTime = e;
        l.Status = LessonStatus.Planned;
        l.Note = reason.Trim();
        db.Audit(session, "Dars ko'chirildi", nameof(LessonOccurrence), l.Id, $"{old:dd.MM.yyyy} → {newDate:dd.MM.yyyy}", reason);
        db.SaveChanges();
    }

    public LessonOccurrence AddExtraLesson(UserSession session, int groupId, int subjectId, DateOnly date, int lessonNumber,
        TimeOnly start, TimeOnly end, string? room, int academicHours, string reason)
    {
        session.Demand(Permission.EditCalendar);
        if (end <= start) throw new BusinessRuleException("Tugash vaqti boshlanishdan keyin bo'lishi kerak.");
        using var db = _factory.Create();
        var l = new LessonOccurrence
        {
            GroupId = groupId, SubjectId = subjectId, TeacherUserId = session.UserId, Date = date, LessonNumber = lessonNumber,
            StartTime = start, EndTime = end, Room = room, AcademicHours = Math.Max(1, academicHours),
            Origin = LessonOrigin.Manual, Status = LessonStatus.Planned, IsConfirmed = true, Note = reason,
        };
        db.LessonOccurrences.Add(l);
        db.SaveChanges();
        db.Audit(session, "Qo'shimcha dars qo'shildi", nameof(LessonOccurrence), l.Id, $"{date:dd.MM.yyyy} {lessonNumber}-dars", reason);
        db.SaveChanges();
        return l;
    }

    public void SetConducted(UserSession session, int lessonId, bool conducted)
    {
        session.Demand(Permission.TakeAttendance);
        using var db = _factory.Create();
        var l = Load(db, lessonId);
        if (l.Status == LessonStatus.Cancelled && conducted) throw new BusinessRuleException("Bekor qilingan darsni avval tiklang.");
        l.Status = conducted ? LessonStatus.Conducted : LessonStatus.Planned;
        if (conducted) l.IsConfirmed = true;
        db.Audit(session, conducted ? "Dars o'tildi deb belgilandi" : "Dars o'tilmagan holatiga qaytarildi", nameof(LessonOccurrence), l.Id, $"{l.Date:dd.MM.yyyy}");
        db.SaveChanges();
    }

    /// <summary>Himoyalanmagan avtomatik darsni o'chirish (masalan, noto'g'ri yaratilgan).</summary>
    public void DeleteLesson(UserSession session, int lessonId, string reason)
    {
        session.Demand(Permission.EditCalendar);
        using var db = _factory.Create();
        var l = Load(db, lessonId);
        if (db.AttendanceRecords.Any(a => a.LessonOccurrenceId == lessonId))
            throw new BusinessRuleException("Davomat olingan darsni o'chirib bo'lmaydi.");
        if (l.Status == LessonStatus.Conducted)
            throw new BusinessRuleException("O'tilgan darsni o'chirib bo'lmaydi.");
        db.LessonOccurrences.Remove(l);
        db.Audit(session, "Dars o'chirildi", nameof(LessonOccurrence), lessonId, $"{l.Date:dd.MM.yyyy}", reason);
        db.SaveChanges();
    }
}
