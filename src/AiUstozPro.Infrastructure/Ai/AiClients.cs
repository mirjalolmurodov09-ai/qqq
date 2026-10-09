using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiUstozPro.Application.Ai;

namespace AiUstozPro.Infrastructure.Ai;

/// <summary>HTTP orqali ishlaydigan AI mijozlari uchun umumiy asos: kutish vaqti, xatolarni tushunarli qilish.</summary>
public abstract class HttpAiClient : IAiClient
{
    protected readonly AiSettings Settings;
    protected readonly HttpClient Http;

    protected HttpAiClient(AiSettings settings, HttpMessageHandler? handler)
    {
        Settings = settings;
        Http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        Http.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    }

    protected abstract HttpRequestMessage BuildRequest(string system, IReadOnlyList<AiChatMessage> messages);
    protected abstract AiReply ParseResponse(JsonNode json);
    protected virtual string? ParseError(JsonNode? json)
    {
        try
        {
            var err = json?["error"];
            if (err is JsonObject o) return o["message"]?.ToString() ?? o.ToJsonString();
            return err?.ToString();
        }
        catch (InvalidOperationException) { return null; }
    }

    public async Task<AiReply> CompleteAsync(string system, IReadOnlyList<AiChatMessage> messages, CancellationToken ct)
    {
        using var req = BuildRequest(system, messages);
        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiException($"AI xizmati {Settings.TimeoutSeconds} soniyada javob bermadi. Internetni tekshiring yoki keyinroq urinib ko'ring.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException(Settings.Provider == AiProviderKind.Ollama
                ? $"Lokal Ollama serveriga ulanib bo'lmadi ({Settings.EffectiveBaseUrl}). Ollama ishga tushirilganini tekshiring."
                : "AI xizmatiga ulanib bo'lmadi. Internet aloqasini tekshiring.", ex);
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JsonNode? json = null;
            try { json = string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body); }
            catch (JsonException) { /* xato matni JSON bo'lmasligi mumkin */ }

            if (!resp.IsSuccessStatusCode)
            {
                var detail = ParseError(json) ?? (body.Length > 300 ? body[..300] : body);
                throw new AiException(resp.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "API kaliti noto'g'ri yoki ruxsat yo'q. Sozlamalarda kalitni tekshiring.",
                    HttpStatusCode.NotFound => $"Model yoki manzil topilmadi (\"{Settings.EffectiveModel}\"). Model nomini tekshiring. {detail}",
                    HttpStatusCode.TooManyRequests => "AI xizmati so'rovlar chegarasiga yetdi (429). Birozdan keyin urinib ko'ring.",
                    HttpStatusCode.BadRequest => $"So'rov rad etildi: {detail}",
                    _ when (int)resp.StatusCode >= 500 => $"AI xizmatida vaqtinchalik nosozlik ({(int)resp.StatusCode}). Keyinroq urinib ko'ring.",
                    _ => $"AI xizmati xatosi ({(int)resp.StatusCode}): {detail}",
                });
            }
            if (json is null) throw new AiException("AI xizmati bo'sh javob qaytardi.");
            try
            {
                var reply = ParseResponse(json);
                if (string.IsNullOrWhiteSpace(reply.Text)) throw new AiException("AI xizmati matnsiz javob qaytardi.");
                return reply;
            }
            catch (AiException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or FormatException)
            {
                throw new AiException("AI javobini o'qib bo'lmadi (kutilmagan format).", ex);
            }
        }
    }

    protected static StringContent JsonBody(JsonObject o)
        => new(o.ToJsonString(), Encoding.UTF8, "application/json");

    protected static int? Int(JsonNode? n) => n is null ? null : n.GetValue<int>();
}

/// <summary>Anthropic Messages API (POST /v1/messages).</summary>
public sealed class AnthropicClient : HttpAiClient
{
    private readonly string _apiKey;
    public AnthropicClient(AiSettings s, string apiKey, HttpMessageHandler? handler = null) : base(s, handler) => _apiKey = apiKey;

    protected override HttpRequestMessage BuildRequest(string system, IReadOnlyList<AiChatMessage> messages)
    {
        var arr = new JsonArray();
        foreach (var m in messages) arr.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["max_tokens"] = Settings.MaxTokens,
            ["system"] = system,
            ["messages"] = arr,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, Settings.EffectiveBaseUrl + "/v1/messages") { Content = JsonBody(body) };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    protected override AiReply ParseResponse(JsonNode json)
    {
        var sb = new StringBuilder();
        foreach (var block in json["content"]!.AsArray())
            if (block?["type"]?.GetValue<string>() == "text") sb.Append(block["text"]!.GetValue<string>());
        return new AiReply(sb.ToString(), Int(json["usage"]?["input_tokens"]), Int(json["usage"]?["output_tokens"]),
            json["model"]?.GetValue<string>() ?? Settings.EffectiveModel);
    }
}

/// <summary>OpenAI Chat Completions API (POST /v1/chat/completions) va unga mos serverlar.</summary>
public sealed class OpenAiClient : HttpAiClient
{
    private readonly string _apiKey;
    public OpenAiClient(AiSettings s, string apiKey, HttpMessageHandler? handler = null) : base(s, handler) => _apiKey = apiKey;

    protected override HttpRequestMessage BuildRequest(string system, IReadOnlyList<AiChatMessage> messages)
    {
        var arr = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages) arr.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["max_tokens"] = Settings.MaxTokens,
            ["messages"] = arr,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, Settings.EffectiveBaseUrl + "/v1/chat/completions") { Content = JsonBody(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return req;
    }

    protected override AiReply ParseResponse(JsonNode json)
        => new(json["choices"]![0]!["message"]!["content"]!.GetValue<string>(),
               Int(json["usage"]?["prompt_tokens"]), Int(json["usage"]?["completion_tokens"]),
               json["model"]?.GetValue<string>() ?? Settings.EffectiveModel);
}

/// <summary>Lokal Ollama serveri (POST /api/chat, stream=false). Internet talab qilmaydi.</summary>
public sealed class OllamaClient : HttpAiClient
{
    public OllamaClient(AiSettings s, HttpMessageHandler? handler = null) : base(s, handler) { }

    protected override HttpRequestMessage BuildRequest(string system, IReadOnlyList<AiChatMessage> messages)
    {
        var arr = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = system } };
        foreach (var m in messages) arr.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content });
        var body = new JsonObject
        {
            ["model"] = Settings.EffectiveModel,
            ["messages"] = arr,
            ["stream"] = false,
            ["options"] = new JsonObject { ["num_predict"] = Settings.MaxTokens },
        };
        return new HttpRequestMessage(HttpMethod.Post, Settings.EffectiveBaseUrl + "/api/chat") { Content = JsonBody(body) };
    }

    protected override AiReply ParseResponse(JsonNode json)
        => new(json["message"]!["content"]!.GetValue<string>(), Int(json["prompt_eval_count"]), Int(json["eval_count"]),
               json["model"]?.GetValue<string>() ?? Settings.EffectiveModel);

    protected override string? ParseError(JsonNode? json) => json?["error"]?.ToString();
}

public static class AiClientFactory
{
    public static IAiClient Create(AiSettings settings, string? apiKey, HttpMessageHandler? handler = null) => settings.Provider switch
    {
        AiProviderKind.Anthropic => new AnthropicClient(settings, Require(apiKey), handler),
        AiProviderKind.OpenAI => new OpenAiClient(settings, Require(apiKey), handler),
        AiProviderKind.Ollama => new OllamaClient(settings, handler),
        _ => throw new AiException("AI yordamchi o'chirilgan. Sozlamalar → AI xizmati bo'limida provayderni tanlang."),
    };

    private static string Require(string? key)
        => string.IsNullOrWhiteSpace(key) ? throw new AiException("API kaliti kiritilmagan. Sozlamalar → AI xizmati bo'limida kalitni saqlang.") : key.Trim();
}
