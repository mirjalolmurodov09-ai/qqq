using System.Net;
using System.Text;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure;
using AiUstozPro.Infrastructure.Services;
using AiUstozPro.Infrastructure.Voice;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Tests;

public class VoiceTests
{
    [Fact]
    public void WAV_sarlavhasi_togri()
    {
        var pcm = new byte[3200];
        var wav = WavEncoder.FromPcm16(pcm, 16000, 1);
        Assert.Equal(44 + 3200, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32000, BitConverter.ToInt32(wav, 28)); // byte rate
        Assert.Equal(3200, BitConverter.ToInt32(wav, 40));
        Assert.Equal(0, WavEncoder.PeakLevel(pcm));
        var loud = new byte[] { 0x00, 0x40, 0x00, 0x00 }; // 16384
        Assert.Equal(0.5, WavEncoder.PeakLevel(loud), 3);
    }

    [Fact]
    public async Task OpenAI_transkripsiya_multipart_sorov_va_javob()
    {
        var h = new FakeHandler((req, body) => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"text\":\" Salom, algoritm nima? \"}", Encoding.UTF8, "application/json") });
        var t = new OpenAiTranscriber("sk-x", null, "whisper-1", h);
        var text = await t.TranscribeAsync(WavEncoder.FromPcm16(new byte[3200]), "uz", default);
        Assert.Equal("Salom, algoritm nima?", text);
        var (req, body) = h.Calls.Single();
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.StartsWith("multipart/form-data", req.Content!.Headers.ContentType!.ToString());
        Assert.Contains("name=model", body);
        Assert.Contains("whisper-1", body);
        Assert.Contains("name=language", body);
        Assert.Contains("RIFF", body);
    }

    [Fact]
    public async Task Transkripsiya_xatolari()
    {
        Assert.Throws<AiException>(() => new OpenAiTranscriber(""));
        var bad = FakeHandler.Json("{\"error\":{\"message\":\"x\"}}", HttpStatusCode.Unauthorized);
        var t = new OpenAiTranscriber("k", handler: bad);
        var ex = await Assert.ThrowsAsync<AiException>(() => t.TranscribeAsync(WavEncoder.FromPcm16(new byte[100]), "uz", default));
        Assert.Contains("kaliti", ex.Message);
        var empty = FakeHandler.Json("{\"text\":\"\"}");
        await Assert.ThrowsAsync<AiException>(() => new OpenAiTranscriber("k", handler: empty).TranscribeAsync(WavEncoder.FromPcm16(new byte[100]), null, default));
        await Assert.ThrowsAsync<AiException>(() => new OpenAiTranscriber("k", handler: empty).TranscribeAsync(new byte[10], null, default));
    }

    [Fact]
    public async Task Ovoz_sozlamalari_saqlanadi_va_tekshiriladi()
    {
        using var f = new Fixture();
        Assert.False(f.App.Voice.GetSettings().Enabled);
        f.App.Voice.SaveSettings(f.Admin, new VoiceSettings { Enabled = true, Stt = SttProvider.Windows, Language = "ru", Rate = 2, Volume = 55, TtsVoice = "Microsoft Irina (ru-RU)" });
        var s = f.App.Voice.GetSettings();
        Assert.True(s.Enabled);
        Assert.Equal(SttProvider.Windows, s.Stt);
        Assert.Equal("ru", s.Language);
        Assert.Equal(55, s.Volume);
        Assert.Equal("Microsoft Irina (ru-RU)", s.TtsVoice);
        Assert.Throws<BusinessRuleException>(() => f.App.Voice.SaveSettings(f.Admin, new VoiceSettings { Volume = 150 }));
        Assert.Throws<BusinessRuleException>(() => f.App.Voice.SaveSettings(f.Admin, new VoiceSettings { Language = "de" }));
        await Assert.ThrowsAsync<AiException>(() => f.App.Voice.TranscribeOpenAiAsync(f.Admin, WavEncoder.FromPcm16(new byte[100]), default));
    }
}

public class LessonModeTests
{
    [Fact]
    public void Dars_konteksti_jurnal_materiallar_va_hisobot()
    {
        using var f = new Fixture();
        var plan = f.App.Curriculum.GetOrCreatePlan(f.Admin, f.Calendar.Id, f.Group.Id, f.Subject.Id);
        f.App.Curriculum.ImportTopics(f.Admin, plan.Id, new[] { new CurriculumTopic { OrderNo = 1, Title = "Kompyuter tarixi", Hours = 2, ExpectedOutcome = "Avlodlarni biladi" } }, true);
        f.App.Curriculum.ApplyPlacement(f.Admin, plan.Id);
        var lesson = f.Lessons[0];
        var topicId = f.App.Curriculum.ListTopics(plan.Id)[0].Topic.Id;

        var file = System.IO.Path.Combine(f.Dir.Path, "taqdimot.pptx");
        File.WriteAllText(file, "x");
        f.App.Lessons.AddMaterial(f.Admin, topicId, "", file);
        f.App.Lessons.AddMaterial(f.Admin, topicId, "Video", "https://example.org/video");
        Assert.Throws<BusinessRuleException>(() => f.App.Lessons.AddMaterial(f.Admin, topicId, "", System.IO.Path.Combine(f.Dir.Path, "yoq.pdf")));

        f.AttendanceAt(lesson.Date).Save(f.Admin, lesson.Id, f.Students.Select((s, i) => new AttendanceMark(s.Id, i == 0 ? AttendanceStatus.AbsentExcused : AttendanceStatus.Present, null)).ToList(), null);
        f.App.Lessons.SaveLog(f.Admin, lesson.Id, "Izoh", "Uyga: 3-mashq", "Xulosa matni", null);
        f.App.Lessons.SaveLog(f.Admin, lesson.Id, "Izoh 2", "Uyga: 3-mashq", "Xulosa matni", "So'rov");

        var c = f.App.Lessons.GetContext(f.Admin, lesson.Id);
        Assert.Equal("Kompyuter tarixi", c.Topics.Single().Topic.Title);
        Assert.Equal(2, c.Topics[0].Materials.Count);
        Assert.Contains(c.Topics[0].Materials, m => m.IsLink && m.Title == "Video");
        Assert.Contains(c.Topics[0].Materials, m => !m.IsLink && m.Title == "taqdimot.pptx");
        Assert.Equal(2, c.Present);
        Assert.Equal(1, c.Absent);
        Assert.Equal("Izoh 2", c.Log!.Notes);
        using (var db = f.App.Factory.Create()) Assert.Equal(1, db.LessonLogs.Count()); // bitta dars — bitta jurnal

        var report = f.App.Lessons.ReportText(f.Admin, lesson.Id);
        Assert.Contains("Kompyuter tarixi", report);
        Assert.Contains("Uyga: 3-mashq", report);
        Assert.Contains("darsda: 2", report);

        f.App.Auth.CreateUser(f.Admin, "kuz", "K", "Kuzat12345", UserRole.Observer);
        var obs = f.App.Auth.Login("kuz", "Kuzat12345").Session!;
        Assert.Throws<AccessDeniedException>(() => f.App.Lessons.GetContext(obs, lesson.Id));
        Assert.Throws<AccessDeniedException>(() => f.App.Lessons.SaveLog(obs, lesson.Id, "x", null, null, null));
    }

    [Fact]
    public void Tezkor_sorov_formati()
    {
        var text = QuickPoll.Format("2+2?", new[] { ("A) 4", 15), ("B) 5", 5) }, "A) 4");
        Assert.Contains("A) 4: 15 (75%) — to'g'ri javob", text);
        Assert.Contains("B) 5: 5 (25%)", text);
        Assert.Contains("Jami javob: 20", text);
        Assert.Contains("0 (0%)", QuickPoll.Format("?", new[] { ("A", 0) }));
    }

    [Fact]
    public void Sxema_v3_dan_v4_ga()
    {
        using var dir = new TempDir();
        var app = AppServices.Open(dir.Path, new InMemorySecretStore());
        app.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        using (var db = app.Factory.Create())
        {
            db.Database.ExecuteSqlRaw("DROP TABLE \"LessonLogs\";");
            db.Database.ExecuteSqlRaw("DROP TABLE \"TopicMaterials\";");
            db.Database.ExecuteSqlRaw("UPDATE \"AppSettings\" SET \"Value\"='3' WHERE \"Key\"='SchemaVersion';");
        }
        SqliteConnection.ClearAllPools();
        var up = AppServices.Open(dir.Path, new InMemorySecretStore());
        Assert.Equal(3, up.Migration.FromVersion);
        Assert.Equal(4, up.Migration.ToVersion);
        using var db2 = up.Factory.Create();
        Assert.Equal(0, db2.LessonLogs.Count());
        Assert.Equal(0, db2.TopicMaterials.Count());
    }
}
