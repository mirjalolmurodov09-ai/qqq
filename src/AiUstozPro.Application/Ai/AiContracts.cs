namespace AiUstozPro.Application.Ai;

public enum AiProviderKind
{
    Disabled = 0,
    Anthropic = 1,
    OpenAI = 2,
    Ollama = 3,
}

/// <summary>AI xizmati sozlamalari (maxfiy bo'lmagan qismi; API kaliti alohida himoyalangan joyda).</summary>
public sealed record AiSettings
{
    public AiProviderKind Provider { get; init; } = AiProviderKind.Disabled;
    public string Model { get; init; } = "";
    /// <summary>Bo'sh bo'lsa provayderning standart manzili.</summary>
    public string? BaseUrl { get; init; }
    public int MaxTokens { get; init; } = 2000;
    /// <summary>Bir soatdagi so'rovlar chegarasi (xarajatni nazorat qilish uchun).</summary>
    public int HourlyLimit { get; init; } = 30;
    public int TimeoutSeconds { get; init; } = 90;

    public bool RequiresApiKey => Provider is AiProviderKind.Anthropic or AiProviderKind.OpenAI;

    public static string DefaultModel(AiProviderKind p) => p switch
    {
        AiProviderKind.Anthropic => "claude-sonnet-5-5",
        AiProviderKind.OpenAI => "gpt-4o-mini",
        AiProviderKind.Ollama => "llama3.1",
        _ => "",
    };

    public static string DefaultBaseUrl(AiProviderKind p) => p switch
    {
        AiProviderKind.Anthropic => "https://api.anthropic.com",
        AiProviderKind.OpenAI => "https://api.openai.com",
        AiProviderKind.Ollama => "http://localhost:11434",
        _ => "",
    };

    public string EffectiveBaseUrl => (string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl(Provider) : BaseUrl!.Trim()).TrimEnd('/');
    public string EffectiveModel => string.IsNullOrWhiteSpace(Model) ? DefaultModel(Provider) : Model.Trim();

    public string? Validate()
    {
        if (Provider == AiProviderKind.Disabled) return null;
        if (MaxTokens < 100 || MaxTokens > 16000) return "Javob uzunligi (token) 100 dan 16000 gacha bo'lsin.";
        if (HourlyLimit < 1 || HourlyLimit > 1000) return "Soatlik limit 1 dan 1000 gacha bo'lsin.";
        if (TimeoutSeconds < 10 || TimeoutSeconds > 600) return "Kutish vaqti 10–600 soniya bo'lsin.";
        if (!Uri.TryCreate(EffectiveBaseUrl, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http"))
            return "Manzil (URL) noto'g'ri.";
        if (u.Scheme == "http" && Provider != AiProviderKind.Ollama && !u.IsLoopback)
            return "API kaliti shifrlanmagan (http) ulanish orqali yuborilmaydi. https manzilidan foydalaning.";
        return null;
    }

    public static string ProviderName(AiProviderKind p) => p switch
    {
        AiProviderKind.Anthropic => "Claude (Anthropic)",
        AiProviderKind.OpenAI => "OpenAI",
        AiProviderKind.Ollama => "Lokal model (Ollama)",
        _ => "O'chirilgan",
    };
}

public sealed record AiChatMessage(string Role, string Content);

public sealed record AiReply(string Text, int? InputTokens, int? OutputTokens, string Model);

/// <summary>Foydalanuvchiga tushunarli xabar bilan AI xatosi.</summary>
public sealed class AiException : Exception
{
    public AiException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IAiClient
{
    Task<AiReply> CompleteAsync(string system, IReadOnlyList<AiChatMessage> messages, CancellationToken ct);
}

/// <summary>Maxfiy qiymatlarni (API kalitlari) saqlash.</summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _d = new();
    public string? Get(string name) => _d.TryGetValue(name, out var v) ? v : null;
    public void Set(string name, string value) => _d[name] = value;
    public void Delete(string name) => _d.Remove(name);
}
