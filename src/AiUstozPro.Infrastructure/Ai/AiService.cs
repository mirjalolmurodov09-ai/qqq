using System.Globalization;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Ai;

public sealed record AiAskResult(AiConversation Conversation, AiMessage Question, AiMessage Answer);

/// <summary>
/// AI yordamchi: sozlamalar, kalit, so'rovlar limiti va suhbatlar tarixi.
/// AI ishlamasa ham dasturning qolgan qismi ishlayveradi — barcha xatolar <see cref="AiException"/> sifatida qaytadi.
/// </summary>
public sealed class AiService
{
    public const string KeyPrefix = "Ai.";
    private const int HistoryMessages = 12;

    private readonly IDbFactory _factory;
    private readonly ISecretStore _secrets;
    private readonly Func<AiSettings, string?, IAiClient> _clientFactory;
    private readonly Func<DateTime> _utcNow;

    public AiService(IDbFactory factory, ISecretStore secrets,
        Func<AiSettings, string?, IAiClient>? clientFactory = null, Func<DateTime>? utcNow = null)
    {
        _factory = factory;
        _secrets = secrets;
        _clientFactory = clientFactory ?? ((s, k) => AiClientFactory.Create(s, k));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    private static string SecretName(AiProviderKind p) => $"ApiKey.{p}";

    // ---------- Sozlamalar ----------

    public AiSettings GetSettings()
    {
        using var db = _factory.Create();
        var d = db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith(KeyPrefix)).ToDictionary(s => s.Key, s => s.Value);
        string? Get(string k) => d.TryGetValue(KeyPrefix + k, out var v) ? v : null;
        int Int(string k, int def) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        var provider = Enum.TryParse<AiProviderKind>(Get("Provider"), out var p) ? p : AiProviderKind.Disabled;
        return new AiSettings
        {
            Provider = provider,
            Model = Get("Model") ?? AiSettings.DefaultModel(provider),
            BaseUrl = Get("BaseUrl"),
            MaxTokens = Int("MaxTokens", 2000),
            HourlyLimit = Int("HourlyLimit", 30),
            TimeoutSeconds = Int("TimeoutSeconds", 90),
        };
    }

    public void SaveSettings(UserSession session, AiSettings s)
    {
        session.Demand(Permission.ManageSettings);
        var err = s.Validate();
        if (err is not null) throw new BusinessRuleException(err);
        using var db = _factory.Create();
        void Set(string k, string? v)
        {
            var key = KeyPrefix + k;
            var row = db.AppSettings.FirstOrDefault(x => x.Key == key);
            if (v is null) { if (row is not null) db.AppSettings.Remove(row); return; }
            if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = v });
            else row.Value = v;
        }
        Set("Provider", s.Provider.ToString());
        Set("Model", s.Model?.Trim());
        Set("BaseUrl", string.IsNullOrWhiteSpace(s.BaseUrl) ? null : s.BaseUrl.Trim());
        Set("MaxTokens", s.MaxTokens.ToString(CultureInfo.InvariantCulture));
        Set("HourlyLimit", s.HourlyLimit.ToString(CultureInfo.InvariantCulture));
        Set("TimeoutSeconds", s.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        db.Audit(session, "AI sozlamalari o'zgartirildi", nameof(AppSetting), null,
            $"{AiSettings.ProviderName(s.Provider)}, model={s.EffectiveModel}, limit={s.HourlyLimit}/soat");
        db.SaveChanges();
    }

    public bool HasApiKey(AiProviderKind p) => !string.IsNullOrEmpty(_secrets.Get(SecretName(p)));

    public void SetApiKey(UserSession session, AiProviderKind p, string? key)
    {
        session.Demand(Permission.ManageSettings);
        if (string.IsNullOrWhiteSpace(key)) _secrets.Delete(SecretName(p));
        else _secrets.Set(SecretName(p), key.Trim());
        using var db = _factory.Create();
        db.Audit(session, string.IsNullOrWhiteSpace(key) ? "AI API kaliti o'chirildi" : "AI API kaliti saqlandi", nameof(AppSetting), null,
            AiSettings.ProviderName(p)); // kalitning o'zi jurnalga yozilmaydi
        db.SaveChanges();
    }

    public bool IsEnabled
    {
        get
        {
            var s = GetSettings();
            return s.Provider != AiProviderKind.Disabled && (!s.RequiresApiKey || HasApiKey(s.Provider));
        }
    }

    public string StatusText()
    {
        var s = GetSettings();
        if (s.Provider == AiProviderKind.Disabled) return "AI yordamchi o'chirilgan.";
        if (s.RequiresApiKey && !HasApiKey(s.Provider)) return $"{AiSettings.ProviderName(s.Provider)}: API kaliti kiritilmagan.";
        return $"{AiSettings.ProviderName(s.Provider)} · {s.EffectiveModel} · bu soatda {UsedThisHour()} / {s.HourlyLimit} so'rov";
    }

    public int UsedThisHour()
    {
        var since = _utcNow().AddHours(-1);
        using var db = _factory.Create();
        return db.AiMessages.Count(m => m.Role == "assistant" && m.CreatedUtc >= since);
    }

    public async Task<string> TestConnectionAsync(UserSession session, AiSettings? draft, string? draftKey, CancellationToken ct)
    {
        session.Demand(Permission.UseAi);
        var s = draft ?? GetSettings();
        var err = s.Validate();
        if (err is not null) throw new AiException(err);
        var key = string.IsNullOrWhiteSpace(draftKey) ? _secrets.Get(SecretName(s.Provider)) : draftKey;
        var client = _clientFactory(s, key);
        var started = DateTime.UtcNow;
        var reply = await client.CompleteAsync("Faqat bitta so'z bilan javob ber.", new[] { new AiChatMessage("user", "Salom! Ulanishni tekshiryapman. \"Tayyor\" deb javob ber.") }, ct);
        var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
        return $"Ulanish ishlayapti: {reply.Model}, javob {ms} ms. Javob: {Shorten(reply.Text, 60)}";
    }

    // ---------- Suhbatlar ----------

    public List<AiConversation> ListConversations(UserSession session, int take = 200)
    {
        session.Demand(Permission.UseAi);
        using var db = _factory.Create();
        return db.AiConversations.AsNoTracking().Where(c => c.UserId == session.UserId)
                 .OrderByDescending(c => c.UpdatedUtc).Take(take).ToList();
    }

    public List<AiMessage> GetMessages(UserSession session, int conversationId)
    {
        session.Demand(Permission.UseAi);
        using var db = _factory.Create();
        var conv = db.AiConversations.AsNoTracking().FirstOrDefault(c => c.Id == conversationId && c.UserId == session.UserId)
                   ?? throw new BusinessRuleException("Suhbat topilmadi.");
        return db.AiMessages.AsNoTracking().Where(m => m.ConversationId == conv.Id).OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id).ToList();
    }

    public void DeleteConversation(UserSession session, int conversationId)
    {
        session.Demand(Permission.UseAi);
        using var db = _factory.Create();
        var conv = db.AiConversations.FirstOrDefault(c => c.Id == conversationId && c.UserId == session.UserId)
                   ?? throw new BusinessRuleException("Suhbat topilmadi.");
        db.AiConversations.Remove(conv);
        db.Audit(session, "AI suhbati o'chirildi", nameof(AiConversation), conversationId, conv.Title);
        db.SaveChanges();
    }

    /// <summary>O'qituvchi AI javobini o'qib chiqib, tahrirlab tasdiqlaydi. Tasdiqlanmagan matn rasmiy hujjatga chiqmaydi.</summary>
    public AiMessage SaveReviewed(UserSession session, int messageId, string editedContent)
    {
        session.Demand(Permission.UseAi);
        if (string.IsNullOrWhiteSpace(editedContent)) throw new BusinessRuleException("Matn bo'sh.");
        using var db = _factory.Create();
        var m = db.AiMessages.Include(x => x.Conversation).FirstOrDefault(x => x.Id == messageId && x.Role == "assistant")
                ?? throw new BusinessRuleException("Javob topilmadi.");
        if (m.Conversation!.UserId != session.UserId) throw new AccessDeniedException("Bu suhbat sizniki emas.");
        var changed = m.Content != editedContent;
        m.Content = editedContent;
        m.IsReviewed = true;
        m.ReviewedUtc = _utcNow();
        db.Audit(session, "AI javobi o'qituvchi tomonidan tasdiqlandi", nameof(AiMessage), m.Id, changed ? "tahrirlangan" : "o'zgarishsiz");
        db.SaveChanges();
        return m;
    }

    /// <summary>
    /// Savol yuboradi. conversationId null bo'lsa — yangi suhbat. Avvalgi xabarlar kontekst sifatida qo'shiladi.
    /// Savol bazaga faqat javob muvaffaqiyatli olingandan keyin saqlanadi.
    /// </summary>
    public async Task<AiAskResult> AskAsync(UserSession session, int? conversationId, string taskKey, string title, string prompt, CancellationToken ct)
    {
        session.Demand(Permission.UseAi);
        if (string.IsNullOrWhiteSpace(prompt)) throw new BusinessRuleException("Savol matni bo'sh.");
        if (prompt.Length > 60_000) throw new BusinessRuleException("Savol juda uzun (60 000 belgidan oshmasin).");
        var s = GetSettings();
        if (s.Provider == AiProviderKind.Disabled)
            throw new AiException("AI yordamchi o'chirilgan. Sozlamalar → AI xizmati bo'limida provayderni tanlang.");
        var used = UsedThisHour();
        if (used >= s.HourlyLimit)
            throw new AiException($"Soatlik so'rovlar chegarasiga yetildi ({s.HourlyLimit}). Birozdan keyin urinib ko'ring yoki administrator limitni oshirsin.");

        var history = new List<AiChatMessage>();
        if (conversationId is int cid)
        {
            using var db0 = _factory.Create();
            var own = db0.AiConversations.Any(c => c.Id == cid && c.UserId == session.UserId);
            if (!own) throw new BusinessRuleException("Suhbat topilmadi.");
            history = db0.AiMessages.AsNoTracking().Where(m => m.ConversationId == cid)
                .OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id).Take(HistoryMessages).ToList()
                .OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id)
                .Select(m => new AiChatMessage(m.Role, m.Content)).ToList();
        }
        history.Add(new AiChatMessage("user", prompt));

        var client = _clientFactory(s, s.RequiresApiKey ? _secrets.Get(SecretName(s.Provider)) : null);
        var reply = await client.CompleteAsync(PromptTemplates.SystemPrompt, history, ct);

        using var db = _factory.Create();
        AiConversation conv;
        if (conversationId is int id) conv = db.AiConversations.First(c => c.Id == id);
        else
        {
            conv = new AiConversation
            {
                UserId = session.UserId, Title = Shorten(string.IsNullOrWhiteSpace(title) ? prompt : title, 200), TaskKey = taskKey,
                Provider = s.Provider.ToString(), Model = reply.Model, CreatedUtc = _utcNow(),
            };
            db.AiConversations.Add(conv);
        }
        var now = _utcNow();
        conv.UpdatedUtc = now;
        var q = new AiMessage { Conversation = conv, Role = "user", Content = prompt, CreatedUtc = now };
        var a = new AiMessage { Conversation = conv, Role = "assistant", Content = reply.Text, CreatedUtc = now.AddMilliseconds(1),
                                InputTokens = reply.InputTokens, OutputTokens = reply.OutputTokens };
        db.AiMessages.Add(q);
        db.AiMessages.Add(a);
        db.SaveChanges();
        return new AiAskResult(conv, q, a);
    }

    private static string Shorten(string s, int max)
    {
        s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
