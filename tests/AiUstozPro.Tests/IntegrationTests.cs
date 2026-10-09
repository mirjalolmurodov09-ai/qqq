using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aup-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch { /* Windows fayl qulfi — ahamiyatsiz */ }
    }
}

/// <summary>Ma'lumotlar bazasi bilan ishlaydigan integratsion testlar uchun tayyor muhit.</summary>
public sealed class Fixture : IDisposable
{
    public TempDir Dir { get; } = new();
    public AppServices App { get; }
    public UserSession Admin { get; }
    public Group Group { get; }
    public Subject Subject { get; }
    public AcademicCalendar Calendar { get; }
    public List<Student> Students { get; }
    public List<LessonOccurrence> Lessons { get; }

    public Fixture()
    {
        App = AppServices.Open(Dir.Path);
        Admin = App.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        Group = App.Academic.SaveGroup(Admin, new Group { Name = "G-1" });
        Subject = App.Academic.SaveSubject(Admin, new Subject { Name = "Informatika" });
        foreach (var (l, f) in new[] { ("Aliyev", "Test"), ("Boboyeva", "Sinov"), ("Aliyev", "Test") })
            App.Academic.SaveStudent(Admin, new Student { LastName = l, FirstName = f, GroupId = Group.Id });
        Students = App.Academic.ListStudents(Admin, Group.Id);
        Calendar = App.Calendar.SaveCalendar(Admin, new AcademicCalendar
        {
            Name = "2026", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 10, 31),
            WorkingDaysMask = AcademicCalendar.MaskOf(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday),
        });
        App.Calendar.SaveTimetableEntry(Admin, new TimetableEntry
        {
            AcademicCalendarId = Calendar.Id, GroupId = Group.Id, SubjectId = Subject.Id, TeacherUserId = Admin.UserId,
            DayOfWeek = DayOfWeek.Monday, LessonNumber = 1, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(9, 50),
        }, false);
        App.Calendar.ApplyLessons(Admin, Calendar.Id, Group.Id, Subject.Id);
        Lessons = App.Calendar.ListLessons(Group.Id, Subject.Id);
    }

    public AttendanceService AttendanceAt(DateOnly today) => new(App.Factory, () => today);

    public void Dispose() => Dir.Dispose();
}

public class IntegrationTests
{
    [Fact]
    public void Uchidan_uchigacha_ssenariy()
    {
        using var dir = new TempDir();
        var log = new List<string>();
        SmokeScenario.Run(dir.Path, log.Add);
        Assert.Contains(log, l => l.Contains("barcha tekshiruvlar"));
    }

    [Fact]
    public void Bir_xil_ismli_oquvchilar_alohida_saqlanadi()
    {
        using var f = new Fixture();
        Assert.Equal(3, f.Students.Count);
        Assert.Equal(2, f.Students.Count(s => s.LastName == "Aliyev"));
        Assert.True(f.App.Academic.HasSameNameInGroup(f.Group.Id, "Aliyev", "Test", null));
    }

    [Fact]
    public void Oquvchi_raqami_takrorlanmaydi()
    {
        using var f = new Fixture();
        f.App.Academic.SaveStudent(f.Admin, new Student { LastName = "A", FirstName = "B", StudentNumber = "N1", GroupId = f.Group.Id });
        Assert.Throws<BusinessRuleException>(() =>
            f.App.Academic.SaveStudent(f.Admin, new Student { LastName = "C", FirstName = "D", StudentNumber = "N1", GroupId = f.Group.Id }));
    }

    [Fact]
    public void Davomat_bir_darsga_bir_marta_baza_darajasida_ham()
    {
        using var f = new Fixture();
        var lesson = f.Lessons[0];
        var att = f.AttendanceAt(lesson.Date);
        att.Save(f.Admin, lesson.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        att.Save(f.Admin, lesson.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        using (var db = f.App.Factory.Create())
        {
            Assert.Equal(1, db.AttendanceRecords.Count(a => a.LessonOccurrenceId == lesson.Id));
            // To'g'ridan-to'g'ri ikkinchi yozuv qo'shishga urinish — UNIQUE indeks rad etadi.
            db.AttendanceRecords.Add(new AttendanceRecord { StudentId = f.Students[0].Id, LessonOccurrenceId = lesson.Id, Status = AttendanceStatus.Late, RecordedByUserId = f.Admin.UserId });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        Assert.Throws<BusinessRuleException>(() => att.Save(f.Admin, lesson.Id,
            [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null), new AttendanceMark(f.Students[0].Id, AttendanceStatus.Late, null)], null));
    }

    [Fact]
    public void Kuzatuvchi_davomatni_ozgartira_olmaydi_va_faqat_ruxsat_berilgan_guruhni_koradi()
    {
        using var f = new Fixture();
        f.App.Auth.CreateUser(f.Admin, "rahbar", "Rahbar", "Rahbar12345", UserRole.Observer);
        var observer = f.App.Auth.Login("rahbar", "Rahbar12345").Session!;
        var lesson = f.Lessons[0];
        Assert.Throws<AccessDeniedException>(() => f.AttendanceAt(lesson.Date).Save(observer, lesson.Id,
            [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null));
        Assert.Empty(f.App.Academic.ListGroups(observer));
        Assert.Throws<AccessDeniedException>(() => f.App.Reports.AttendanceMatrix(observer, f.Group.Id, null, f.Calendar.StartDate, f.Calendar.EndDate));

        f.App.Auth.SetObserverGroups(f.Admin, observer.UserId, [f.Group.Id]);
        Assert.Single(f.App.Academic.ListGroups(observer));
        var report = f.App.Reports.AttendanceMatrix(observer, f.Group.Id, null, f.Calendar.StartDate, f.Calendar.EndDate);
        Assert.Equal(3, report.Rows.Count);
    }

    [Fact]
    public void Oqituvchi_faqat_oz_darsida_davomat_oladi()
    {
        using var f = new Fixture();
        f.App.Auth.CreateUser(f.Admin, "boshqa", "Boshqa o'qituvchi", "Boshqa12345", UserRole.Teacher);
        var other = f.App.Auth.Login("boshqa", "Boshqa12345").Session!;
        var lesson = f.Lessons[0];
        Assert.Throws<AccessDeniedException>(() => f.AttendanceAt(lesson.Date).Save(other, lesson.Id,
            [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null));
    }

    [Fact]
    public void Kelajakdagi_va_bekor_qilingan_darsga_davomat_olinmaydi()
    {
        using var f = new Fixture();
        var lesson = f.Lessons[3];
        Assert.Throws<BusinessRuleException>(() => f.AttendanceAt(lesson.Date.AddDays(-1)).Save(f.Admin, lesson.Id,
            [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null));
        f.App.Calendar.CancelLesson(f.Admin, lesson.Id, "test");
        Assert.Throws<BusinessRuleException>(() => f.AttendanceAt(lesson.Date).Save(f.Admin, lesson.Id,
            [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null));
    }

    [Fact]
    public void Davomat_tuzatilishi_audit_jurnaliga_sabab_bilan_yoziladi()
    {
        using var f = new Fixture();
        var lesson = f.Lessons[0];
        var att = f.AttendanceAt(lesson.Date);
        att.Save(f.Admin, lesson.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        att.Save(f.Admin, lesson.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.AbsentExcused, null)], "Ma'lumotnoma keltirdi");
        using var db = f.App.Factory.Create();
        var log = db.AuditLogs.Single(a => a.Action == "Davomat tuzatildi");
        Assert.Equal("Ma'lumotnoma keltirdi", log.Reason);
        Assert.Equal("admin", log.UserLogin);
    }

    [Fact]
    public void Faqat_administrator_davomat_yozuvini_ochiradi()
    {
        using var f = new Fixture();
        var lesson = f.Lessons[0];
        f.AttendanceAt(lesson.Date).Save(f.Admin, lesson.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        int recordId;
        using (var db = f.App.Factory.Create()) recordId = db.AttendanceRecords.Single().Id;
        f.App.Auth.CreateUser(f.Admin, "oqituvchi", "O'qituvchi", "Oqituvchi123", UserRole.Teacher);
        var teacher = f.App.Auth.Login("oqituvchi", "Oqituvchi123").Session!;
        Assert.Throws<AccessDeniedException>(() => f.App.Attendance.DeleteRecord(teacher, recordId, "x"));
        f.App.Attendance.DeleteRecord(f.Admin, recordId, "Xato kiritilgan");
        using var db2 = f.App.Factory.Create();
        Assert.Empty(db2.AttendanceRecords);
        Assert.Contains(db2.AuditLogs, a => a.Action == "Davomat yozuvi o'chirildi" && a.Reason == "Xato kiritilgan");
    }

    [Fact]
    public void Kalendar_qayta_hisoblanganda_davomatli_dars_saqlanadi_boshqalari_yangilanadi()
    {
        using var f = new Fixture();
        var withAtt = f.Lessons[1];
        f.AttendanceAt(withAtt.Date).Save(f.Admin, withAtt.Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        // Endi ikkala dars sanasini ham bayram deb belgilaymiz.
        foreach (var l in new[] { f.Lessons[1], f.Lessons[2] })
            f.App.Calendar.SaveException(f.Admin, new CalendarException { AcademicCalendarId = f.Calendar.Id, Kind = CalendarExceptionKind.ExtraDayOff, StartDate = l.Date, Title = "test" });
        var preview = f.App.Calendar.PreviewLessons(f.Calendar.Id, f.Group.Id, f.Subject.Id);
        Assert.Single(preview.Reconcile.ToRemove);
        Assert.Single(preview.Reconcile.Conflicts);
        var rec = f.App.Calendar.ApplyLessons(f.Admin, f.Calendar.Id, f.Group.Id, f.Subject.Id);
        var after = f.App.Calendar.ListLessons(f.Group.Id, f.Subject.Id);
        Assert.Contains(after, l => l.Id == withAtt.Id);
        Assert.DoesNotContain(after, l => l.Id == f.Lessons[2].Id);
        Assert.Equal(f.Lessons.Count - 1, after.Count);
    }

    [Fact]
    public void Qolda_kochirilgan_va_qoshimcha_darslar_qayta_hisoblashda_saqlanadi()
    {
        using var f = new Fixture();
        var l = f.Lessons[0];
        f.App.Calendar.MoveLesson(f.Admin, l.Id, l.Date.AddDays(2), null, null, null, "Bino ta'mirda");
        var extra = f.App.Calendar.AddExtraLesson(f.Admin, f.Group.Id, f.Subject.Id, new DateOnly(2026, 9, 5), 2,
            new TimeOnly(10, 0), new TimeOnly(11, 20), null, 2, "Qo'shimcha mashg'ulot");
        var rec = f.App.Calendar.ApplyLessons(f.Admin, f.Calendar.Id, f.Group.Id, f.Subject.Id);
        Assert.False(rec.HasChanges);
        var after = f.App.Calendar.ListLessons(f.Group.Id, f.Subject.Id);
        Assert.Contains(after, x => x.Id == l.Id && x.Date == l.Date.AddDays(2) && x.Origin == LessonOrigin.Moved);
        Assert.Contains(after, x => x.Id == extra.Id);
    }

    [Fact]
    public void Jadval_yozuvi_davomat_bolsa_ochirilmaydi_faolsizlantiriladi()
    {
        using var f = new Fixture();
        var entryId = f.Lessons[0].TimetableEntryId!.Value;
        f.AttendanceAt(f.Lessons[0].Date).Save(f.Admin, f.Lessons[0].Id, [new AttendanceMark(f.Students[0].Id, AttendanceStatus.Present, null)], null);
        var msg = f.App.Calendar.DeleteTimetableEntry(f.Admin, entryId);
        Assert.Contains("faolsizlantirildi", msg);
        var after = f.App.Calendar.ListLessons(f.Group.Id, f.Subject.Id);
        Assert.Single(after);
    }

    [Fact]
    public void Qulflangan_mavzu_qayta_joylashtirishda_ozgarmaydi()
    {
        using var f = new Fixture();
        var plan = f.App.Curriculum.GetOrCreatePlan(f.Admin, f.Calendar.Id, f.Group.Id, f.Subject.Id);
        f.App.Curriculum.ImportTopics(f.Admin, plan.Id, new[]
        {
            new CurriculumTopic { OrderNo = 1, Title = "A", Hours = 2 },
            new CurriculumTopic { OrderNo = 2, Title = "B", Hours = 2 },
            new CurriculumTopic { OrderNo = 3, Title = "C", Hours = 2 },
        }, replace: true);
        f.App.Curriculum.ApplyPlacement(f.Admin, plan.Id);
        var topics = f.App.Curriculum.ListTopics(plan.Id);
        f.App.Curriculum.MarkTopicCompleted(f.Admin, topics[0].Topic.Id, null, null);
        f.App.Calendar.CancelLesson(f.Admin, f.Lessons[0].Id, "test"); // o'tilgan mavzu darsi bekor qilinsa ham
        f.App.Curriculum.ApplyPlacement(f.Admin, plan.Id);
        var after = f.App.Curriculum.ListTopics(plan.Id);
        Assert.Equal(topics[0].LessonDates, after[0].LessonDates);
        Assert.Equal(TopicStatus.Completed, after[0].Topic.Status);

        // Almashtirish bilan import o'tilgan mavzuni o'chirmaydi.
        f.App.Curriculum.ImportTopics(f.Admin, plan.Id, new[] { new CurriculumTopic { OrderNo = 2, Title = "Yangi", Hours = 2 } }, replace: true);
        var titles = f.App.Curriculum.ListTopics(plan.Id).Select(t => t.Topic.Title).ToList();
        Assert.Equal(new[] { "A", "Yangi" }, titles);
    }

    [Fact]
    public void Kirish_bloklash_va_kop_marta_xato_parol()
    {
        using var f = new Fixture();
        for (int i = 0; i < AuthService.MaxFailedAttempts; i++) Assert.False(f.App.Auth.Login("admin", "xato12345").Success);
        var r = f.App.Auth.Login("admin", "Admin12345");
        Assert.False(r.Success);
        Assert.Contains("daqiqadan", r.Error);
        Assert.Throws<BusinessRuleException>(() => f.App.Auth.SetBlocked(f.Admin, f.Admin.UserId, true));
    }

    [Fact]
    public void Qayta_ochilganda_malumotlar_saqlanadi_va_sxema_versiyasi_tekshiriladi()
    {
        using var f = new Fixture();
        SqliteConnection.ClearAllPools();
        var reopened = AppServices.Open(f.Dir.Path);
        Assert.False(reopened.Migration.Created);
        Assert.Equal(DatabaseMigrator.CurrentVersion, reopened.Migration.ToVersion);
        var s = reopened.Auth.Login("admin", "Admin12345").Session!;
        Assert.Equal(3, reopened.Academic.ListStudents(s, f.Group.Id).Count);

        reopened.Settings.Set(DatabaseMigrator.VersionKey, "999");
        Assert.Throws<InvalidOperationException>(() => AppServices.Open(f.Dir.Path));
    }

    [Fact]
    public void Notogri_fayldan_tiklash_rad_etiladi()
    {
        using var f = new Fixture();
        var bad = System.IO.Path.Combine(f.Dir.Path, "bad.db");
        File.WriteAllText(bad, "bu baza emas");
        Assert.Throws<BusinessRuleException>(() => f.App.Backup.Restore(f.Admin, bad));
        Assert.Equal(3, f.App.Academic.ListStudents(f.Admin, f.Group.Id).Count);
    }

    [Fact]
    public void Avtomatik_zaxira_kuniga_bir_marta()
    {
        using var f = new Fixture();
        Assert.NotNull(f.App.Backup.AutoBackupIfDue());
        Assert.Null(f.App.Backup.AutoBackupIfDue());
    }
}
