using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Application.Testing;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Tests;

public class TestLogicTests
{
    private static List<QuestionKey> Keys(int n, int options = 4)
        => Enumerable.Range(1, n).Select(i => new QuestionKey(i, 1, Enumerable.Range(i * 10, options).ToList(), i * 10 + (i % options))).ToList();

    [Theory]
    [InlineData(100, 5)] [InlineData(86, 5)] [InlineData(85.9, 4)] [InlineData(71, 4)] [InlineData(70.9, 3)] [InlineData(56, 3)] [InlineData(55.9, 2)] [InlineData(0, 2)]
    public void Standart_mezon(double pct, int grade) => Assert.Equal(grade, GradingScale.Default.Grade(pct));

    [Fact]
    public void Mezon_tekshiruvi()
    {
        Assert.Null(new GradingScale(90, 75, 60).Validate());
        Assert.NotNull(new GradingScale(70, 75, 60).Validate());
        Assert.NotNull(new GradingScale(101, 75, 60).Validate());
    }

    [Fact]
    public void Birinchi_variant_asl_tartibda_boshqalari_deterministik_aralashadi()
    {
        var keys = Keys(10);
        var v1 = VariantBuilder.Build(5, 1, keys, true, true);
        Assert.Equal(keys.Select(k => k.QuestionId), v1.Questions.Select(q => q.QuestionId));
        Assert.Equal(keys[0].OptionIds, v1.Questions[0].OptionIds);
        var v2a = VariantBuilder.Build(5, 2, keys, true, true);
        var v2b = VariantBuilder.Build(5, 2, keys, true, true);
        Assert.Equal(v2a.Questions.Select(q => q.QuestionId), v2b.Questions.Select(q => q.QuestionId));
        Assert.Equal(v2a.Questions.SelectMany(q => q.OptionIds), v2b.Questions.SelectMany(q => q.OptionIds));
        Assert.NotEqual(v1.Questions.Select(q => q.QuestionId), v2a.Questions.Select(q => q.QuestionId));
        Assert.Equal(keys.Select(k => k.QuestionId).OrderBy(x => x), v2a.Questions.Select(q => q.QuestionId).OrderBy(x => x));
        var noShuffle = VariantBuilder.Build(5, 3, keys, false, false);
        Assert.Equal(keys.Select(k => k.QuestionId), noShuffle.Questions.Select(q => q.QuestionId));
    }

    [Fact]
    public void Javoblar_satri_oqiladi()
    {
        var (s, e) = AnswerSheet.Parse("a b-C,d", 5);
        Assert.Null(e);
        Assert.Equal(new int?[] { 0, 1, null, 2, 3 }, s.ToArray());
        (s, e) = AnswerSheet.Parse("АВСД", 4); // kirill harflari
        Assert.Null(e);
        Assert.Equal(new int?[] { 0, 1, 2, 3 }, s.ToArray());
        Assert.NotNull(AnswerSheet.Parse("ABC", 4).Error);
        Assert.NotNull(AnswerSheet.Parse("ABCZ", 4).Error);
        Assert.NotNull(AnswerSheet.Parse("ABCE", 4, maxOptions: 4).Error);
        Assert.Equal("AB-C", AnswerSheet.Format(new int?[] { 0, 1, null, 2 }));
    }

    [Fact]
    public void Baholash_variant_boyicha_togri()
    {
        var keys = Keys(4);
        var dict = keys.ToDictionary(k => k.QuestionId);
        var v = VariantBuilder.Build(9, 2, keys, true, true);
        var key = v.AnswerKey(dict);
        var all = AnswerSheet.Parse(key, 4).Selections;
        var full = Scorer.Score(v, dict, all);
        Assert.Equal(4, full.Score);
        Assert.Equal(100, full.Percent);
        var half = AnswerSheet.Parse(key[..2] + "--", 4).Selections;
        var r = Scorer.Score(v, dict, half);
        Assert.Equal(2, r.Score);
        Assert.Equal(50, r.Percent);
        Assert.Null(r.Answers[3].SelectedOptionId);
    }

    [Fact]
    public void AI_JSON_savollari_tahlil_qilinadi()
    {
        var text = "Mana savollar:\n```json\n[{\"savol\":\"Bit nima?\",\"variantlar\":[\"0 yoki 1\",\"8 bit\",\"Fayl\",\"Papka\"],\"togri\":0}," +
                   "{\"savol\":\"\",\"variantlar\":[\"a\",\"b\"],\"togri\":0}," +
                   "{\"savol\":\"X?\",\"variantlar\":[\"a\",\"b\"],\"togri\":5}," +
                   "{\"question\":\"Bayt?\",\"options\":[\"8 bit\",\"4 bit\"],\"correct\":0}]\n```";
        var (qs, errors) = AiQuestionParser.Parse(text);
        Assert.Equal(2, qs.Count);
        Assert.Equal(2, errors.Count);
        Assert.Equal("Bit nima?", qs[0].Text);
        Assert.Equal(0, qs[0].CorrectIndex);
        Assert.Single(AiQuestionParser.Parse("javob yo'q").Errors);
        Assert.Contains("JSON", AiQuestionParser.BuildPrompt(new() { Topic = "Bit", Count = 5 }));
    }
}

public class AssessmentServiceTests
{
    private static (Fixture F, Assessment A) Ready(int questions = 4, int variants = 2)
    {
        var f = new Fixture();
        var a = f.App.Tests.Save(f.Admin, new Assessment { Title = "Nazorat 1", SubjectId = f.Subject.Id, VariantCount = variants, TimeLimitMinutes = 10 });
        for (int i = 1; i <= questions; i++)
            f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(0, $"Savol {i}", 1, new[]
            {
                new OptionInput($"to'g'ri {i}", true), new OptionInput($"xato {i}a", false), new OptionInput($"xato {i}b", false), new OptionInput($"xato {i}c", false),
            }));
        f.App.Tests.SetStatus(f.Admin, a.Id, AssessmentStatus.Ready);
        return (f, a);
    }

    [Fact]
    public void Savol_validatsiyasi()
    {
        using var f = new Fixture();
        var a = f.App.Tests.Save(f.Admin, new Assessment { Title = "T", SubjectId = f.Subject.Id });
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(0, "Q", 1, new[] { new OptionInput("a", true) })));
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(0, "Q", 1, new[] { new OptionInput("a", true), new OptionInput("b", true) })));
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(0, "Q", 1, new[] { new OptionInput("a", true), new OptionInput("A", false) })));
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.SetStatus(f.Admin, a.Id, AssessmentStatus.Ready)); // savollar yo'q
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.Save(f.Admin, new Assessment { Title = "T", SubjectId = f.Subject.Id, Grade5Min = 60, Grade4Min = 70, Grade3Min = 50 }));
    }

    [Fact]
    public void AI_savollari_tasdiqlanmaguncha_test_tayyor_bolmaydi()
    {
        using var f = new Fixture();
        var a = f.App.Tests.Save(f.Admin, new Assessment { Title = "AI test", SubjectId = f.Subject.Id });
        var n = f.App.Tests.AddAiQuestions(f.Admin, a.Id, new[] { new ParsedQuestion("Bit?", new[] { "0/1", "8" }, 0), new ParsedQuestion("Bayt?", new[] { "8 bit", "2 bit" }, 0) });
        Assert.Equal(2, n);
        var ex = Assert.Throws<BusinessRuleException>(() => f.App.Tests.SetStatus(f.Admin, a.Id, AssessmentStatus.Ready));
        Assert.Contains("tasdiqlanmagan", ex.Message);
        foreach (var q in f.App.Tests.Get(a.Id).Questions) f.App.Tests.ApproveQuestion(f.Admin, q.Id);
        f.App.Tests.SetStatus(f.Admin, a.Id, AssessmentStatus.Ready);
        Assert.Equal(AssessmentStatus.Ready, f.App.Tests.Get(a.Id).Status);
    }

    [Fact]
    public void Tayyor_bolmagan_testga_natija_yozilmaydi()
    {
        using var f = new Fixture();
        var a = f.App.Tests.Save(f.Admin, new Assessment { Title = "T", SubjectId = f.Subject.Id });
        f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(0, "Q", 1, new[] { new OptionInput("a", true), new OptionInput("b", false) }));
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0 }, ResultMethod.ManualEntry));
    }

    [Fact]
    public void Natija_baho_tasdiqlash_va_sabab()
    {
        var (f, a) = Ready();
        using var _ = f;
        var (v2, keys, _) = f.App.Tests.GetVariant(a.Id, 2);
        var key = v2.AnswerKey(keys);
        var sel = AnswerSheet.Parse(key[..3] + "-", 4).Selections; // 3/4 = 75% → 4
        var r = f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 5), 2, sel, ResultMethod.ManualEntry);
        Assert.Equal(3, r.Score);
        Assert.Equal(75, r.Percent);
        Assert.Equal(4, r.AutoGrade);
        Assert.False(r.IsConfirmed);
        Assert.Null(r.FinalGrade);

        Assert.Throws<BusinessRuleException>(() => f.App.Tests.Confirm(f.Admin, r.Id, 5, null)); // farq qiladi — sabab kerak
        f.App.Tests.Confirm(f.Admin, r.Id, 5, "Og'zaki qo'shimcha javob berdi");
        var rows = f.App.Tests.ListResults(f.Admin, a.Id, f.Group.Id);
        var row = rows.Single(x => x.Student.Id == f.Students[0].Id);
        Assert.Equal(5, row.Result!.FinalGrade);
        Assert.True(row.Result.IsConfirmed);
        Assert.Equal(key[..3] + "-", row.Answers);
        // Tasdiqlangan natija ustidan yozib bo'lmaydi, qayta topshirish esa mumkin.
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 6), 2, sel, ResultMethod.ManualEntry));
        var r2 = f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 6), 1, new int?[] { 0, 0, 0, 0 }, ResultMethod.ManualEntry, newAttempt: true);
        Assert.Equal(2, r2.AttemptNo);
        Assert.Equal(100, r2.Percent);
        using var db = f.App.Factory.Create();
        Assert.Contains(db.AuditLogs, l => l.Action == "Test bahosi tasdiqlandi" && l.Reason == "Og'zaki qo'shimcha javob berdi");
    }

    [Fact]
    public void Natijalar_bor_testning_savollari_ozgarmaydi_nusxa_olinadi()
    {
        var (f, a) = Ready(2);
        using var _ = f;
        f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0, 1 }, ResultMethod.ManualEntry);
        var q = f.App.Tests.Get(a.Id).Questions[0];
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.DeleteQuestion(f.Admin, q.Id));
        Assert.Throws<BusinessRuleException>(() => f.App.Tests.SaveQuestion(f.Admin, a.Id, new QuestionInput(q.Id, "o'zgardi", 1, new[] { new OptionInput("a", true), new OptionInput("b", false) })));
        var copy = f.App.Tests.Duplicate(f.Admin, a.Id);
        Assert.Equal(2, f.App.Tests.Get(copy.Id).Questions.Count);
        Assert.Equal(AssessmentStatus.Draft, copy.Status);
        f.App.Tests.DeleteQuestion(f.Admin, f.App.Tests.Get(copy.Id).Questions[0].Id);
        Assert.Equal(2, f.App.Tests.Get(a.Id).Questions.Count);
    }

    [Fact]
    public void Mezon_ozgarsa_tasdiqlanmagan_avto_baho_qayta_hisoblanadi()
    {
        var (f, a) = Ready(4);
        using var _ = f;
        var r1 = f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0, 0, 0, null }, ResultMethod.ManualEntry); // 75%
        var r2 = f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[1].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0, 0, 0, null }, ResultMethod.ManualEntry);
        f.App.Tests.Confirm(f.Admin, r2.Id, 4, null);
        var current = f.App.Tests.Get(a.Id);
        current.Grade5Min = 75; current.Grade4Min = 60; current.Grade3Min = 40;
        f.App.Tests.Save(f.Admin, current);
        var rows = f.App.Tests.ListResults(f.Admin, a.Id, f.Group.Id);
        Assert.Equal(5, rows.Single(r => r.Student.Id == f.Students[0].Id).Result!.AutoGrade);
        Assert.Equal(4, rows.Single(r => r.Student.Id == f.Students[1].Id).Result!.FinalGrade); // tasdiqlangan o'zgarmadi
    }

    [Fact]
    public void Import_raqam_va_ism_boyicha_xatolar_bilan()
    {
        var (f, a) = Ready(3, 2);
        using var _ = f;
        f.App.Academic.SaveStudent(f.Admin, new Student { LastName = "Sodiqov", FirstName = "Bek", StudentNumber = "S-77", GroupId = f.Group.Id });
        var csv = "O'quvchi;Variant;Javoblar\nS-77;1;AAA\nBoboyeva Sinov;2;AB-\nAliyev Test;1;AAA\nNoma'lum Kishi;1;AAA\nS-77;1;AAA\n";
        var data = CsvReader.Parse(csv);
        var map = ColumnMapper.AutoMap(data.Headers, AssessmentService.ImportFields);
        var rows = f.App.Tests.ValidateImport(a.Id, f.Group.Id, data, map);
        Assert.Null(rows[0].Error);
        Assert.Null(rows[1].Error);
        Assert.Contains("bir nechta", rows[2].Error); // ikkita "Aliyev Test" — raqam talab qilinadi
        Assert.Contains("topilmadi", rows[3].Error);
        Assert.Contains("takrorlangan", rows[4].Error);
        var n = f.App.Tests.ImportResults(f.Admin, a.Id, new DateOnly(2026, 10, 7), rows, newAttempts: false);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Kuzatuvchi_natija_yoza_olmaydi_hisobot_va_chop_etish()
    {
        var (f, a) = Ready(3, 3);
        using var _ = f;
        f.App.Tests.RecordResult(f.Admin, a.Id, f.Students[0].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0, 0, 1 }, ResultMethod.Computer, durationSeconds: 300);
        f.App.Auth.CreateUser(f.Admin, "rahbar", "R", "Rahbar12345", UserRole.Observer);
        var obs = f.App.Auth.Login("rahbar", "Rahbar12345").Session!;
        Assert.Throws<AccessDeniedException>(() => f.App.Tests.RecordResult(obs, a.Id, f.Students[1].Id, new DateOnly(2026, 10, 5), 1, new int?[] { 0, 0, 0 }, ResultMethod.ManualEntry));
        Assert.Throws<AccessDeniedException>(() => f.App.Tests.ListResults(obs, a.Id, f.Group.Id));

        var report = f.App.Tests.ResultsReport(f.Admin, a.Id, f.Group.Id);
        Assert.Equal(3, report.Rows.Count);
        Assert.Contains(report.FooterLines, l => l.Contains("tasdiqlanmagan"));
        var items = f.App.Tests.ItemAnalysis(f.Admin, a.Id, f.Group.Id);
        Assert.Equal("0%", items.Rows[2][4]);
        var keys = f.App.Tests.AnswerKeys(a.Id);
        Assert.Equal(3, keys.Rows.Count);
        Assert.Equal("1A 2A 3A", keys.Rows[0][1]);

        var text = f.App.Tests.PrintableText(a.Id, 2);
        Assert.Contains("2-variant", text);
        Assert.Contains("A) ", text);
        var dir = System.IO.Path.Combine(f.Dir.Path, "chop");
        foreach (var fmt in new[] { ExportFormat.Word, ExportFormat.Pdf })
        {
            var p = System.IO.Path.Combine(dir, "test" + ReportExporter.Extension(fmt));
            ReportExporter.ExportText("Test", new[] { "meta" }, text, fmt, p);
            Assert.True(new FileInfo(p).Length > 500);
        }
        ReportExporter.Export(report, ExportFormat.Excel, System.IO.Path.Combine(dir, "natija.xlsx"));
    }

    [Fact]
    public void Sxema_v2_dan_v3_ga_yangilanadi()
    {
        using var dir = new TempDir();
        var app = AiUstozPro.Infrastructure.AppServices.Open(dir.Path, new AiUstozPro.Application.Ai.InMemorySecretStore());
        app.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        using (var db = app.Factory.Create())
        {
            foreach (var t in new[] { "TestAnswers", "TestResults", "TestOptions", "TestQuestions", "Assessments" })
                db.Database.ExecuteSqlRaw($"DROP TABLE \"{t}\";");
            db.Database.ExecuteSqlRaw("UPDATE \"AppSettings\" SET \"Value\"='2' WHERE \"Key\"='SchemaVersion';");
        }
        SqliteConnection.ClearAllPools();
        var up = AiUstozPro.Infrastructure.AppServices.Open(dir.Path, new AiUstozPro.Application.Ai.InMemorySecretStore());
        Assert.Equal(2, up.Migration.FromVersion);
        Assert.Equal(AiUstozPro.Infrastructure.Data.DatabaseMigrator.CurrentVersion, up.Migration.ToVersion);
        var s = up.Auth.Login("admin", "Admin12345").Session!;
        var subj = up.Academic.SaveSubject(s, new Subject { Name = "Fan" });
        var a = up.Tests.Save(s, new Assessment { Title = "T", SubjectId = subj.Id });
        up.Tests.SaveQuestion(s, a.Id, new QuestionInput(0, "Q", 1, new[] { new OptionInput("a", true), new OptionInput("b", false) }));
        Assert.Single(up.Tests.Get(a.Id).Questions);
    }
}
