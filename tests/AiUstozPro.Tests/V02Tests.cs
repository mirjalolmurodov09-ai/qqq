using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure;
using AiUstozPro.Infrastructure.Ai;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Tests;

/// <summary>Soxta HTTP javob beruvchi: haqiqiy tarmoqqa chiqmasdan provayder mijozlarini tekshirish.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
    public FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) => _respond = respond;

    public static FakeHandler Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new((_, _) => new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((request, body));
        return _respond(request, body);
    }
}

public class AiClientTests
{
    private static readonly AiChatMessage[] Q = { new("user", "Salom") };

    [Fact]
    public async Task Anthropic_sorovi_togri_va_javob_oqiladi()
    {
        var h = FakeHandler.Json("""{"model":"claude-sonnet-5-5","content":[{"type":"text","text":"Assalomu "},{"type":"text","text":"alaykum"}],"usage":{"input_tokens":12,"output_tokens":5}}""");
        var c = new AnthropicClient(new AiSettings { Provider = AiProviderKind.Anthropic, MaxTokens = 500 }, "sk-test", h);
        var r = await c.CompleteAsync("tizim", Q, default);
        Assert.Equal("Assalomu alaykum", r.Text);
        Assert.Equal(12, r.InputTokens);
        var (req, body) = h.Calls.Single();
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
        Assert.Equal("sk-test", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").Single());
        var j = JsonNode.Parse(body)!;
        Assert.Equal("claude-sonnet-5-5", j["model"]!.GetValue<string>());
        Assert.Equal(500, j["max_tokens"]!.GetValue<int>());
        Assert.Equal("tizim", j["system"]!.GetValue<string>());
        Assert.Equal("user", j["messages"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenAI_sorovi_va_javobi()
    {
        var h = FakeHandler.Json("""{"model":"gpt-x","choices":[{"message":{"role":"assistant","content":"Javob"}}],"usage":{"prompt_tokens":3,"completion_tokens":2}}""");
        var c = new OpenAiClient(new AiSettings { Provider = AiProviderKind.OpenAI, Model = "gpt-x" }, "key1", h);
        var r = await c.CompleteAsync("tizim", Q, default);
        Assert.Equal("Javob", r.Text);
        var (req, body) = h.Calls.Single();
        Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("system", JsonNode.Parse(body)!["messages"]![0]!["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ollama_lokal_sorov_kalitsiz()
    {
        var h = FakeHandler.Json("""{"model":"llama3.1","message":{"role":"assistant","content":"Lokal javob"},"prompt_eval_count":7,"eval_count":3}""");
        var c = new OllamaClient(new AiSettings { Provider = AiProviderKind.Ollama }, h);
        var r = await c.CompleteAsync("tizim", Q, default);
        Assert.Equal("Lokal javob", r.Text);
        Assert.Equal("http://localhost:11434/api/chat", h.Calls.Single().Request.RequestUri!.ToString());
        Assert.False(JsonNode.Parse(h.Calls[0].Body)!["stream"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API kaliti")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "vaqtinchalik")]
    [InlineData(HttpStatusCode.NotFound, "Model")]
    public async Task Xatolar_tushunarli_xabarga_aylanadi(HttpStatusCode code, string expected)
    {
        var h = FakeHandler.Json("""{"error":{"type":"x","message":"detail"}}""", code);
        var c = new AnthropicClient(new AiSettings { Provider = AiProviderKind.Anthropic }, "k", h);
        var ex = await Assert.ThrowsAsync<AiException>(() => c.CompleteAsync("s", Q, default));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Tarmoq_uzilishi_AiException_beradi()
    {
        var h = new FakeHandler((_, _) => throw new HttpRequestException("no network"));
        var c = new OllamaClient(new AiSettings { Provider = AiProviderKind.Ollama }, h);
        var ex = await Assert.ThrowsAsync<AiException>(() => c.CompleteAsync("s", Q, default));
        Assert.Contains("Ollama", ex.Message);
    }

    [Fact]
    public async Task Buzilgan_javob_formati_AiException_beradi()
    {
        var h = FakeHandler.Json("""{"unexpected":true}""");
        var c = new OpenAiClient(new AiSettings { Provider = AiProviderKind.OpenAI }, "k", h);
        await Assert.ThrowsAsync<AiException>(() => c.CompleteAsync("s", Q, default));
    }

    [Fact]
    public void Kalitsiz_provayder_yaratilmaydi_va_http_kalit_bilan_taqiqlangan()
    {
        Assert.Throws<AiException>(() => AiClientFactory.Create(new AiSettings { Provider = AiProviderKind.Anthropic }, null));
        Assert.Throws<AiException>(() => AiClientFactory.Create(new AiSettings { Provider = AiProviderKind.Disabled }, null));
        Assert.NotNull(new AiSettings { Provider = AiProviderKind.OpenAI, BaseUrl = "http://example.com" }.Validate());
        Assert.Null(new AiSettings { Provider = AiProviderKind.Ollama, BaseUrl = "http://192.168.1.5:11434" }.Validate());
    }

    [Fact]
    public void Shablonlar_kontekstni_oz_ichiga_oladi()
    {
        var ctx = new AiTaskContext { Subject = "Informatika", Course = "1-kurs", Topic = "Chiziqli algoritmlar", Hours = 4, Count = 15, Level = "murakkab" };
        var p = PromptTemplates.Get("test").Build(ctx);
        Assert.Contains("Chiziqli algoritmlar", p);
        Assert.Contains("15 ta", p);
        Assert.Contains("murakkab", p);
        Assert.All(PromptTemplates.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Build(ctx))));
        Assert.Equal("chat", PromptTemplates.Get("mavjud-emas").Key);
    }

    [Fact]
    public void Anonim_davomatda_ismlar_yoq()
    {
        var stats = new[] { new Anonymizer.StudentStat("Aliyev Vali", 8, 1, 1, 0, 10), new Anonymizer.StudentStat("Karimova Zuhra", 5, 0, 0, 5, 10) };
        var text = Anonymizer.AttendanceSummary("1-kurs", "01.09–30.09", stats);
        Assert.DoesNotContain("Aliyev", text);
        Assert.DoesNotContain("Karimova", text);
        Assert.Contains("O'quvchi 2", text);
        Assert.Single(Anonymizer.FindNames("Vali Aliyev haqida", new[] { "Aliyev", "Karimova" }));
    }
}

public class AiServiceTests
{
    private sealed class FakeClient : IAiClient
    {
        public List<IReadOnlyList<AiChatMessage>> Requests { get; } = new();
        public Exception? Throw { get; set; }
        public Task<AiReply> CompleteAsync(string system, IReadOnlyList<AiChatMessage> messages, CancellationToken ct)
        {
            Requests.Add(messages.ToList());
            if (Throw is not null) throw Throw;
            return Task.FromResult(new AiReply($"javob {Requests.Count}", 10, 20, "fake-model"));
        }
    }

    private static (AppServices App, UserSession Admin, AiService Ai, FakeClient Client, TempDir Dir, InMemorySecretStore Secrets) Setup(DateTime? now = null)
    {
        var dir = new TempDir();
        var secrets = new InMemorySecretStore();
        var app = AppServices.Open(dir.Path, secrets);
        var admin = app.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        var client = new FakeClient();
        var ai = new AiService(app.Factory, secrets, (_, _) => client, now is null ? null : () => now.Value);
        return (app, admin, ai, client, dir, secrets);
    }

    [Fact]
    public async Task Ochirilgan_AI_tushunarli_xato_beradi_qolgan_tizim_ishlaydi()
    {
        var (app, admin, ai, _, dir, _) = Setup();
        using var _d = dir;
        var ex = await Assert.ThrowsAsync<AiException>(() => ai.AskAsync(admin, null, "chat", "", "salom", default));
        Assert.Contains("o'chirilgan", ex.Message);
        Assert.NotNull(app.Academic.SaveGroup(admin, new Group { Name = "G" }));
    }

    [Fact]
    public async Task Suhbat_saqlanadi_va_kontekst_yuboriladi()
    {
        var (_, admin, ai, client, dir, secrets) = Setup();
        using var _d = dir;
        ai.SaveSettings(admin, new AiSettings { Provider = AiProviderKind.Anthropic });
        ai.SetApiKey(admin, AiProviderKind.Anthropic, "sk-1");
        Assert.True(ai.IsEnabled);
        Assert.Equal("sk-1", secrets.Get("ApiKey.Anthropic"));

        var r1 = await ai.AskAsync(admin, null, "explain", "Algoritm", "Algoritm nima?", default);
        var r2 = await ai.AskAsync(admin, r1.Conversation.Id, "chat", "", "Misol keltir", default);
        Assert.Equal(r1.Conversation.Id, r2.Conversation.Id);
        Assert.Equal(3, client.Requests[1].Count); // savol, javob, yangi savol
        var msgs = ai.GetMessages(admin, r1.Conversation.Id);
        Assert.Equal(new[] { "user", "assistant", "user", "assistant" }, msgs.Select(m => m.Role).ToArray());
        Assert.Single(ai.ListConversations(admin));
        Assert.Equal(2, ai.UsedThisHour());

        var reviewed = ai.SaveReviewed(admin, r2.Answer.Id, "Tahrirlangan javob");
        Assert.True(reviewed.IsReviewed);
        Assert.Equal("Tahrirlangan javob", ai.GetMessages(admin, r1.Conversation.Id).Last().Content);

        ai.DeleteConversation(admin, r1.Conversation.Id);
        Assert.Empty(ai.ListConversations(admin));
    }

    [Fact]
    public async Task Soatlik_limit_ishlaydi()
    {
        var (_, admin, ai, _, dir, _) = Setup();
        using var _d = dir;
        ai.SaveSettings(admin, new AiSettings { Provider = AiProviderKind.Ollama, HourlyLimit = 2 });
        await ai.AskAsync(admin, null, "chat", "", "1", default);
        await ai.AskAsync(admin, null, "chat", "", "2", default);
        var ex = await Assert.ThrowsAsync<AiException>(() => ai.AskAsync(admin, null, "chat", "", "3", default));
        Assert.Contains("chegara", ex.Message);
    }

    [Fact]
    public async Task Xatolikda_savol_saqlanmaydi()
    {
        var (_, admin, ai, client, dir, _) = Setup();
        using var _d = dir;
        ai.SaveSettings(admin, new AiSettings { Provider = AiProviderKind.Ollama });
        client.Throw = new AiException("ulanib bo'lmadi");
        await Assert.ThrowsAsync<AiException>(() => ai.AskAsync(admin, null, "chat", "", "salom", default));
        Assert.Empty(ai.ListConversations(admin));
    }

    [Fact]
    public void Kuzatuvchi_AI_dan_foydalana_olmaydi_va_oqituvchi_sozlamani_ozgartirmaydi()
    {
        var (app, admin, ai, _, dir, _) = Setup();
        using var _d = dir;
        app.Auth.CreateUser(admin, "kuzat", "K", "Kuzat12345", UserRole.Observer);
        app.Auth.CreateUser(admin, "oqit", "O", "Oqit123456", UserRole.Teacher);
        var obs = app.Auth.Login("kuzat", "Kuzat12345").Session!;
        var teacher = app.Auth.Login("oqit", "Oqit123456").Session!;
        Assert.Throws<AccessDeniedException>(() => ai.ListConversations(obs));
        Assert.Throws<AccessDeniedException>(() => ai.SaveSettings(teacher, new AiSettings { Provider = AiProviderKind.Ollama }));
        Assert.Empty(ai.ListConversations(teacher));
    }

    [Fact]
    public void API_kaliti_bazaga_va_audit_jurnaliga_yozilmaydi()
    {
        var (app, admin, ai, _, dir, _) = Setup();
        using var _d = dir;
        ai.SetApiKey(admin, AiProviderKind.OpenAI, "sk-SECRET-123");
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(app.Factory.DatabasePath);
        Assert.DoesNotContain("sk-SECRET-123", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("sk-SECRET-123", Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void Windows_hisob_malumotlari_menejeri()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore("AiUstozPro-test-" + Guid.NewGuid().ToString("N") + ":");
        Assert.Null(store.Get("k"));
        store.Set("k", "maxfiy-qiymat-ʻ");
        Assert.Equal("maxfiy-qiymat-ʻ", store.Get("k"));
        store.Delete("k");
        Assert.Null(store.Get("k"));
        store.Delete("k"); // yo'q kalitni o'chirish xato bermaydi
    }
}

public class V02InfrastructureTests
{
    [Fact]
    public void Sxema_v1_dan_v2_ga_malumotlarni_saqlab_yangilanadi()
    {
        using var dir = new TempDir();
        var app = AppServices.Open(dir.Path, new InMemorySecretStore());
        var admin = app.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        app.Academic.SaveGroup(admin, new Group { Name = "Eski guruh" });
        // v1 bazani taqlid qilamiz: AI jadvallarini o'chirib, versiyani 1 ga qaytaramiz.
        using (var db = app.Factory.Create())
        {
            db.Database.ExecuteSqlRaw("DROP TABLE \"AiMessages\";");
            db.Database.ExecuteSqlRaw("DROP TABLE \"AiConversations\";");
            db.Database.ExecuteSqlRaw("UPDATE \"AppSettings\" SET \"Value\"='1' WHERE \"Key\"='SchemaVersion';");
            Assert.False(DatabaseMigrator.TableExists(db, "AiMessages"));
        }
        SqliteConnection.ClearAllPools();
        var upgraded = AppServices.Open(dir.Path, new InMemorySecretStore());
        Assert.Equal(1, upgraded.Migration.FromVersion);
        Assert.Equal(2, upgraded.Migration.ToVersion);
        Assert.True(File.Exists(upgraded.Migration.BackupPath));
        var s = upgraded.Auth.Login("admin", "Admin12345").Session!;
        Assert.Single(upgraded.Academic.ListGroups(s));
        using var db2 = upgraded.Factory.Create();
        db2.AiConversations.Add(new AiConversation { UserId = s.UserId, Title = "t", Messages = { new AiMessage { Role = "user", Content = "x" } } });
        db2.SaveChanges();
        Assert.Equal(1, db2.AiMessages.Count());
        Assert.Contains(db2.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type='index' AND tbl_name='AiMessages'").ToList(),
            n => n.StartsWith("IX_AiMessages"));
    }

    [Fact]
    public void Word_hisobot_va_matn_ochiladi_va_sxemaga_mos()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Path);
        var t = new ReportTable { Title = "Davomat jurnali", Landscape = true };
        t.SubtitleLines.Add("Guruh: 101");
        t.Columns.AddRange(new[] { "№", "F.I.Sh.", "Holat" });
        t.Rows.Add(new[] { "1", "Oʻrinboyev Gʻayrat", "+" });
        t.FooterLines.Add("Izoh");
        var p1 = Path.Combine(dir.Path, "a.docx");
        ReportExporter.Export(t, ExportFormat.Word, p1);
        var p2 = Path.Combine(dir.Path, "b.docx");
        DocxWriter.WriteText("Dars ishlanmasi", new[] { "AI yordamida tayyorlandi" }, "# Maqsad\n- **Bilim**: algoritm\n```python\nprint('salom')\n```\nOddiy matn", p2);
        var validator = new OpenXmlValidator();
        foreach (var p in new[] { p1, p2 })
        {
            using var doc = WordprocessingDocument.Open(p, false);
            var errors = validator.Validate(doc).Select(e => e.Description).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
        using (var d1 = WordprocessingDocument.Open(p1, false))
            Assert.Contains("Oʻrinboyev Gʻayrat", d1.MainDocumentPart!.Document.InnerText);
        using var d2 = WordprocessingDocument.Open(p2, false);
        Assert.Contains("print('salom')", d2.MainDocumentPart!.Document.InnerText);
        Assert.Equal(ExportFormat.Word, ReportExporter.FormatFromPath("x.DOCX"));
    }

    [Fact]
    public void Shifrlangan_zaxira_toliq_aylanma_va_notogri_parol()
    {
        using var dir = new TempDir();
        var app = AppServices.Open(dir.Path, new InMemorySecretStore());
        var admin = app.Auth.CreateInitialAdministrator("admin", "Admin", "Admin12345");
        app.Academic.SaveGroup(admin, new Group { Name = "Saqlanadigan guruh" });
        var path = Path.Combine(dir.Path, "flesh", "nusxa" + BackupCrypto.Extension);
        app.Backup.ExportEncrypted(admin, path, "ZaxiraParol1");
        Assert.True(BackupCrypto.IsEncrypted(path));
        Assert.DoesNotContain("Saqlanadigan guruh", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Throws<BusinessRuleException>(() => app.Backup.ExportEncrypted(admin, path + "2", "qisqa"));

        app.Academic.SaveGroup(admin, new Group { Name = "Keyin qo'shilgan" });
        Assert.Throws<BusinessRuleException>(() => app.Backup.Restore(admin, path, "notogri-parol"));
        Assert.Throws<BusinessRuleException>(() => app.Backup.Restore(admin, path, null));
        Assert.Equal(2, app.Academic.ListGroups(admin).Count); // muvaffaqiyatsiz urinish ma'lumotni buzmadi

        app.Backup.Restore(admin, path, "ZaxiraParol1");
        SqliteConnection.ClearAllPools();
        var reopened = AppServices.Open(dir.Path, new InMemorySecretStore());
        var s = reopened.Auth.Login("admin", "Admin12345").Session!;
        Assert.Equal(new[] { "Saqlanadigan guruh" }, reopened.Academic.ListGroups(s).Select(g => g.Name).ToArray());

        // Fayl o'zgartirilsa (bitta bayt) — GCM tegi buni aniqlaydi.
        var bytes = File.ReadAllBytes(path);
        bytes[^10] ^= 0xFF;
        var tampered = Path.Combine(dir.Path, "buzilgan" + BackupCrypto.Extension);
        File.WriteAllBytes(tampered, bytes);
        Assert.Throws<BusinessRuleException>(() => BackupCrypto.DecryptFile(tampered, Path.Combine(dir.Path, "x.db"), "ZaxiraParol1"));
    }

    [Fact]
    public void Anonim_davomat_hisoboti_ismlarsiz()
    {
        using var f = new Fixture();
        f.AttendanceAt(f.Lessons[0].Date).Save(f.Admin, f.Lessons[0].Id,
            f.Students.Select(s => new AttendanceMark(s.Id, AttendanceStatus.Present, null)).ToList(), null);
        var text = f.App.Reports.AnonymousAttendance(f.Admin, f.Group.Id, null, f.Calendar.StartDate, f.Calendar.EndDate);
        Assert.DoesNotContain("Aliyev", text);
        Assert.Contains("O'quvchi 3", text);
    }
}
