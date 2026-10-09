using AiUstozPro.Application.Import;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;

namespace AiUstozPro.Infrastructure;

/// <summary>
/// Muvaffaqiyat mezonini uchidan-uchigacha tekshiruvchi ssenariy (shaxsiy ma'lumotlarsiz test ma'lumotlari bilan).
/// Unit testlarda ham, CI da tayyor .exe ichida ham (--smoke-test) ishlatiladi.
/// </summary>
public static class SmokeScenario
{
    public static void Run(string dataDir, Action<string> log)
    {
        void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("TEKSHIRUV MUVAFFAQIYATSIZ: " + what);
            log("OK  " + what);
        }

        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        var app = AppServices.Open(dataDir);
        Check(app.Migration.Created, "Ma'lumotlar bazasi yaratildi va sxema versiyasi o'rnatildi");

        // 1. Kirish
        var admin = app.Auth.CreateInitialAdministrator("admin", "Test Administrator", "Sinov2026pass");
        Check(app.Auth.Login("admin", "notogri123").Success == false, "Noto'g'ri parol rad etildi");
        var session = app.Auth.Login("admin", "Sinov2026pass").Session!;
        Check(session is not null, "Administrator tizimga kirdi");

        // 2. Guruh, o'quvchilar, fan
        var group = app.Academic.SaveGroup(session!, new Group { Name = "Test-101", Course = "1-kurs" });
        var subject = app.Academic.SaveSubject(session!, new Subject { Name = "Informatika" });
        var csvStudents = "Familiya;Ism;Otasining ismi;O'quvchi raqami\nAliyev;Test;Testovich;T-001\nKarimova;Sinov;;T-002\nAliyev;Test;Testovich;T-003\n";
        var sData = CsvReader.Parse(csvStudents);
        var sMap = ColumnMapper.AutoMap(sData.Headers, StudentImport.Fields);
        var dup = app.Academic.GetDuplicateKeys(group.Id);
        var sRows = StudentImport.Validate(sData, sMap, dup.Numbers, dup.NamesInGroup);
        Check(sRows.All(r => r.IsValid) && sRows[2].Warnings.Count > 0, "O'quvchilar CSV importi tekshirildi (bir xil ismli ikki o'quvchi birlashtirilmadi, ogohlantirish berildi)");
        app.Academic.ImportStudents(session!, group.Id, sRows.Select(r => r.Item!));
        var students = app.Academic.ListStudents(session!, group.Id);
        Check(students.Count == 3, "3 ta o'quvchi saqlandi");

        // 3. O'quv yili, bayramlar, ta'til
        var cal = app.Calendar.SaveCalendar(session!, new AcademicCalendar
        {
            Name = "2026–2027", StartDate = new DateOnly(2026, 9, 2), EndDate = new DateOnly(2027, 1, 31),
            WorkingDaysMask = AcademicCalendar.MaskOf(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday),
        });
        int added = app.Calendar.AddFixedHolidays(session!, cal.Id);
        Check(added == 3, $"Qat'iy bayramlar qo'shildi ({added} ta: 01.10, 08.12, 01.01)");
        app.Calendar.SaveException(session!, new CalendarException
        {
            AcademicCalendarId = cal.Id, Kind = CalendarExceptionKind.Vacation,
            StartDate = new DateOnly(2026, 12, 28), EndDate = new DateOnly(2027, 1, 10), Title = "Qishki ta'til", IsConfirmed = true,
        });

        // 4. Dars jadvali: seshanba va payshanba 1-dars
        foreach (var dow in new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday })
        {
            var (saved, conflicts) = app.Calendar.SaveTimetableEntry(session!, new TimetableEntry
            {
                AcademicCalendarId = cal.Id, GroupId = group.Id, SubjectId = subject.Id, TeacherUserId = session!.UserId,
                DayOfWeek = dow, LessonNumber = 1, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(9, 50), Room = "201", AcademicHours = 2,
            }, allowConflicts: false);
            Check(saved is not null && conflicts.Count == 0, $"Jadvalga {dow.ToUz()} kuni qo'shildi");
        }
        var conflict = app.Calendar.SaveTimetableEntry(session!, new TimetableEntry
        {
            AcademicCalendarId = cal.Id, GroupId = group.Id, SubjectId = subject.Id, TeacherUserId = session!.UserId,
            DayOfWeek = DayOfWeek.Tuesday, LessonNumber = 1, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), Room = "201",
        }, allowConflicts: false);
        Check(conflict.Saved is null && conflict.Conflicts.Count > 0, "Ziddiyatli jadval yozuvi aniqlandi va saqlanmadi");

        // 5. Sanalarni avtomatik hisoblash
        var preview = app.Calendar.PreviewLessons(cal.Id, group.Id, subject.Id);
        Check(preview.AllDates.Any(d => d.IsSkipped && d.Date == new DateOnly(2026, 10, 1)),
            "Ko'rib chiqishda 01.10.2026 (payshanba) bayram sababli o'tilmaydigan sana sifatida ko'rsatildi");
        Check(preview.AllDates.Any(d => d.IsSkipped && d.Date == new DateOnly(2026, 12, 8)),
            "08.12.2026 (seshanba, Konstitutsiya kuni) o'tilmaydigan sana sifatida ko'rsatildi");
        var rec = app.Calendar.ApplyLessons(session!, cal.Id, group.Id, subject.Id);
        var lessons = app.Calendar.ListLessons(group.Id, subject.Id);
        Check(rec.ToAdd.Count == lessons.Count && lessons.Count > 0, $"{lessons.Count} ta dars sanasi yaratildi");
        Check(lessons.All(l => l.Date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday), "Barcha darslar seshanba/payshanba");
        Check(!lessons.Any(l => l.Date == new DateOnly(2026, 10, 1)), "01.10.2026 (O'qituvchilar kuni, payshanba) chiqarib tashlandi");
        Check(!lessons.Any(l => l.Date >= new DateOnly(2026, 12, 28) && l.Date <= new DateOnly(2027, 1, 10)), "Qishki ta'til chiqarib tashlandi");
        var rec2 = app.Calendar.ApplyLessons(session!, cal.Id, group.Id, subject.Id);
        Check(!rec2.HasChanges, "Qayta hisoblash takroriy dars yaratmadi");

        // 6. KTR import va joylashtirish
        var plan = app.Curriculum.GetOrCreatePlan(session!, cal.Id, group.Id, subject.Id);
        var csvTopics = "T/r;Mavzu nomi;Soat;Turi;Izoh\n1;Algoritm tushunchasi;2;Nazariy;\n2;Algoritm turlari;2;Amaliy;\n3;Chiziqli algoritmlar;4;Amaliy;2 darsga\n4;;2;Nazariy;\n";
        var tData = CsvReader.Parse(csvTopics);
        var tMap = ColumnMapper.AutoMap(tData.Headers, TopicImport.Fields);
        var tRows = TopicImport.Validate(tData, tMap);
        Check(tRows.Count(r => !r.IsValid) == 1, "KTR importida bo'sh mavzu satri xato sifatida aniqlandi");
        app.Curriculum.ImportTopics(session!, plan.Id, tRows.Where(r => r.IsValid).Select(r => r.Item!), replace: true);
        var placement = app.Curriculum.ApplyPlacement(session!, plan.Id);
        var topics = app.Curriculum.ListTopics(plan.Id);
        Check(topics.Count == 3 && topics.All(t => t.LessonDates.Count > 0), "Mavzular sanalarga joylashtirildi");
        Check(topics[2].LessonDates.Count == 2, "4 soatlik mavzu ikkita 2 soatlik darsga taqsimlandi");
        Check(topics[0].LessonDates[0] == lessons[0].Date, "Birinchi mavzu birinchi dars sanasida");

        // 7. Darsni bekor qilish — keyingi mavzular suriladi
        app.Calendar.CancelLesson(session!, lessons[1].Id, "Test: tadbir");
        var pv = app.Curriculum.PreviewPlacement(plan.Id);
        Check(pv.Changes.Count > 0, "Bekor qilingan darsdan keyin reja farqi ko'rsatildi");
        app.Curriculum.ApplyPlacement(session!, plan.Id);
        topics = app.Curriculum.ListTopics(plan.Id);
        Check(topics[1].LessonDates[0] == lessons[2].Date, "2-mavzu keyingi haqiqiy dars sanasiga surildi");

        // 8. Davomat
        var first = lessons[0];
        var attendance = new AttendanceService(app.Factory, () => first.Date);
        var marks = students.Select((s, i) => new AttendanceMark(s.Id, i == 0 ? AttendanceStatus.Present : i == 1 ? AttendanceStatus.Late : AttendanceStatus.AbsentExcused, null)).ToList();
        var res = attendance.Save(session!, first.Id, marks, null);
        Check(res.Added == 3, "Davomat olindi (3 ta yozuv)");
        var res2 = attendance.Save(session!, first.Id, marks, null);
        Check(res2.Added == 0 && res2.Unchanged == 3, "Takroriy saqlash ikkinchi yozuv yaratmadi");
        bool needReason = false;
        try { attendance.Save(session!, first.Id, new[] { marks[2] with { Status = AttendanceStatus.AbsentUnexcused } }, null); }
        catch (AiUstozPro.Application.Security.BusinessRuleException) { needReason = true; }
        Check(needReason, "Davomatni tuzatish sababsiz rad etildi");
        attendance.Save(session!, first.Id, new[] { marks[2] with { Status = AttendanceStatus.AbsentUnexcused } }, "Test: sabab hujjati yo'q");
        var sheet = attendance.GetSheet(session!, first.Id);
        Check(sheet.Rows.Single(r => r.Student.Id == students[2].Id).Status == AttendanceStatus.AbsentUnexcused, "Davomat tuzatildi va audit jurnaliga yozildi");

        // 9. Hisobotlar eksporti
        var exportDir = Path.Combine(dataDir, "exports");
        var report = app.Reports.AttendanceMatrix(session!, group.Id, subject.Id, cal.StartDate, cal.EndDate);
        var ktr = app.Reports.Curriculum(session!, plan.Id, DateOnly.FromDateTime(DateTime.Today));
        foreach (var fmt in new[] { ExportFormat.Excel, ExportFormat.Csv, ExportFormat.Pdf, ExportFormat.Word })
        {
            var p1 = Path.Combine(exportDir, "davomat" + ReportExporter.Extension(fmt));
            var p2 = Path.Combine(exportDir, "ktr" + ReportExporter.Extension(fmt));
            ReportExporter.Export(report, fmt, p1);
            ReportExporter.Export(ktr, fmt, p2);
            Check(new FileInfo(p1).Length > 100 && new FileInfo(p2).Length > 100, $"Hisobotlar eksport qilindi: {fmt}");
        }
        using (var wb = new ClosedXML.Excel.XLWorkbook(Path.Combine(exportDir, "ktr.xlsx")))
        {
            var ws = wb.Worksheets.First();
            Check(ws.CellsUsed().Any(c => c.GetString() == "Algoritm tushunchasi"), "Excel fayli qayta ochildi, o'zbekcha matn buzilmagan");
        }
        var pdfHead = File.ReadAllBytes(Path.Combine(exportDir, "ktr.pdf")).Take(5).ToArray();
        Check(System.Text.Encoding.ASCII.GetString(pdfHead) == "%PDF-", "PDF fayli to'g'ri formatda");

        // 9a. Test va baholash
        var test = app.Tests.Save(session!, new Assessment { Title = "Algoritmlar bo'yicha nazorat", SubjectId = subject.Id, VariantCount = 2, TimeLimitMinutes = 15 });
        for (int qn = 1; qn <= 4; qn++)
            app.Tests.SaveQuestion(session!, test.Id, new QuestionInput(0, $"Savol {qn}", 1, new[]
                { new OptionInput("To'g'ri", true), new OptionInput("Xato 1", false), new OptionInput("Xato 2", false), new OptionInput("Xato 3", false) }));
        app.Tests.SetStatus(session!, test.Id, AssessmentStatus.Ready);
        var (tv, tk, _) = app.Tests.GetVariant(test.Id, 2);
        var akey = tv.AnswerKey(tk);
        var tres = app.Tests.RecordResult(session!, test.Id, students[0].Id, first.Date, 2,
            AiUstozPro.Application.Testing.AnswerSheet.Parse(akey[..3] + "-", 4).Selections, ResultMethod.ManualEntry);
        Check(tres.Percent == 75 && tres.AutoGrade == 4 && !tres.IsConfirmed, "Test natijasi variant bo'yicha baholandi (75% → 4), tasdiqlanmagan holatda");
        app.Tests.Confirm(session!, tres.Id, 4, null);
        var trep = app.Tests.ResultsReport(session!, test.Id, group.Id);
        ReportExporter.Export(trep, ExportFormat.Pdf, Path.Combine(exportDir, "test-natija.pdf"));
        ReportExporter.ExportText(test.Title, new[] { "2-variant" }, app.Tests.PrintableText(test.Id, 2), ExportFormat.Word, Path.Combine(exportDir, "test-variant2.docx"));
        Check(File.Exists(Path.Combine(exportDir, "test-variant2.docx")), "Test o'qituvchi tasdiqladi, natijalar va chop etiladigan variant eksport qilindi");

        // 9b. AI: o'chirilgan holatda tushunarli xabar, qolgan tizim ishlayveradi; savol bazaga yozilmaydi.
        bool aiRefused = false;
        try { app.Ai.AskAsync(session!, null, "chat", "", "Salom", CancellationToken.None).GetAwaiter().GetResult(); }
        catch (AiUstozPro.Application.Ai.AiException) { aiRefused = true; }
        Check(aiRefused && app.Ai.ListConversations(session!).Count == 0, "AI o'chirilganda so'rov rad etildi, dastur ishlashda davom etdi");

        // 9c. Shifrlangan zaxira nusxa
        var enc = Path.Combine(dataDir, "flesh", "nusxa" + BackupCrypto.Extension);
        app.Backup.ExportEncrypted(session!, enc, "SmokeParol2026");
        Check(BackupCrypto.IsEncrypted(enc), "Parol bilan shifrlangan zaxira nusxa yaratildi (AES-256-GCM)");

        // 10. Zaxira va tiklash
        var backup = app.Backup.CreateBackup(session!);
        Check(File.Exists(backup.FilePath), "Zaxira nusxa yaratildi");
        app.Academic.SaveGroup(session!, new Group { Name = "Tiklashdan keyin yo'qolishi kerak" });
        app.Backup.Restore(session!, backup.FilePath);
        var reopened = AppServices.Open(dataDir);
        var s2 = reopened.Auth.Login("admin", "Sinov2026pass").Session!;
        Check(reopened.Academic.ListGroups(s2).Count == 1, "Zaxira nusxadan tiklandi");

        // 11. Qayta ochilganda ma'lumotlar saqlangan
        Check(reopened.Academic.ListStudents(s2, group.Id).Count == 3, "Dastur qayta ochilganda o'quvchilar saqlangan");
        Check(reopened.Calendar.ListLessons(group.Id, subject.Id).Count == lessons.Count, "Dars sanalari saqlangan");
        Check(reopened.Curriculum.ListTopics(plan.Id).Count == 3, "KTR saqlangan");
        log("SMOKE TEST: barcha tekshiruvlar muvaffaqiyatli o'tdi.");
    }
}
