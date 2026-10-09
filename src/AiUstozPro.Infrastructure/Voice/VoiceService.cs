using System.Globalization;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Infrastructure.Ai;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Voice;

public enum SttProvider
{
    /// <summary>OpenAI transkripsiya (internet, o'zbek tilini tushunadi).</summary>
    OpenAI = 1,
    /// <summary>Windows o'rnatilgan nutqni aniqlash (internetsiz, faqat o'rnatilgan tillar: odatda rus/ingliz).</summary>
    Windows = 2,
}

public sealed record VoiceSettings
{
    public bool Enabled { get; init; }
    public SttProvider Stt { get; init; } = SttProvider.OpenAI;
    /// <summary>"uz", "ru" yoki "en".</summary>
    public string Language { get; init; } = "uz";
    public string? TtsVoice { get; init; }
    /// <summary>-10..10</summary>
    public int Rate { get; init; }
    /// <summary>0..100</summary>
    public int Volume { get; init; } = 80;
    /// <summary>Bitta yozuvning maksimal davomiyligi (soniya).</summary>
    public int MaxRecordSeconds { get; init; } = 60;

    public string? Validate()
    {
        if (Rate < -10 || Rate > 10) return "Tezlik -10 dan 10 gacha.";
        if (Volume < 0 || Volume > 100) return "Ovoz balandligi 0 dan 100 gacha.";
        if (MaxRecordSeconds < 5 || MaxRecordSeconds > 300) return "Yozuv davomiyligi 5–300 soniya.";
        if (Language is not ("uz" or "ru" or "en")) return "Til: uz, ru yoki en.";
        return null;
    }
}

/// <summary>Ovozli yordamchi sozlamalari va nutqni matnga aylantirish xizmati.</summary>
public sealed class VoiceService
{
    private const string Prefix = "Voice.";
    private readonly IDbFactory _factory;
    private readonly ISecretStore _secrets;
    private readonly AiService _ai;
    private readonly HttpMessageHandler? _handler;

    public VoiceService(IDbFactory factory, ISecretStore secrets, AiService ai, HttpMessageHandler? handler = null)
    {
        _factory = factory; _secrets = secrets; _ai = ai; _handler = handler;
    }

    public VoiceSettings GetSettings()
    {
        using var db = _factory.Create();
        var d = db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith(Prefix)).ToDictionary(s => s.Key, s => s.Value);
        string? Get(string k) => d.TryGetValue(Prefix + k, out var v) ? v : null;
        int Int(string k, int def) => int.TryParse(Get(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        return new VoiceSettings
        {
            Enabled = Get("Enabled") == "1",
            Stt = Enum.TryParse<SttProvider>(Get("Stt"), out var p) ? p : SttProvider.OpenAI,
            Language = Get("Language") ?? "uz",
            TtsVoice = Get("TtsVoice"),
            Rate = Int("Rate", 0),
            Volume = Int("Volume", 80),
            MaxRecordSeconds = Int("MaxRecordSeconds", 60),
        };
    }

    public void SaveSettings(UserSession session, VoiceSettings s)
    {
        session.Demand(Permission.UseAi);
        var err = s.Validate();
        if (err is not null) throw new BusinessRuleException(err);
        using var db = _factory.Create();
        void Set(string k, string? v)
        {
            var key = Prefix + k;
            var row = db.AppSettings.FirstOrDefault(x => x.Key == key);
            if (v is null) { if (row is not null) db.AppSettings.Remove(row); return; }
            if (row is null) db.AppSettings.Add(new Domain.AppSetting { Key = key, Value = v });
            else row.Value = v;
        }
        Set("Enabled", s.Enabled ? "1" : "0");
        Set("Stt", s.Stt.ToString());
        Set("Language", s.Language);
        Set("TtsVoice", string.IsNullOrWhiteSpace(s.TtsVoice) ? null : s.TtsVoice);
        Set("Rate", s.Rate.ToString(CultureInfo.InvariantCulture));
        Set("Volume", s.Volume.ToString(CultureInfo.InvariantCulture));
        Set("MaxRecordSeconds", s.MaxRecordSeconds.ToString(CultureInfo.InvariantCulture));
        db.Audit(session, "Ovozli yordamchi sozlamalari o'zgartirildi", nameof(Domain.AppSetting), null,
            $"yoqilgan={s.Enabled}, aniqlash={s.Stt}, til={s.Language}");
        db.SaveChanges();
    }

    public bool HasOpenAiKey => !string.IsNullOrEmpty(_secrets.Get("ApiKey.OpenAI"));

    /// <summary>OpenAI orqali transkripsiya. Kalit AI sozlamalaridagi OpenAI kaliti.</summary>
    public Task<string> TranscribeOpenAiAsync(UserSession session, byte[] wav, CancellationToken ct)
    {
        session.Demand(Permission.UseAi);
        var s = GetSettings();
        var ai = _ai.GetSettings();
        var baseUrl = ai.Provider == AiProviderKind.OpenAI ? ai.BaseUrl : null;
        var t = new OpenAiTranscriber(_secrets.Get("ApiKey.OpenAI") ?? "", baseUrl, handler: _handler);
        return t.TranscribeAsync(wav, s.Language, ct);
    }
}
