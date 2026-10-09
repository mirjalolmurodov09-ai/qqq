using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AiUstozPro.Application.Ai;

namespace AiUstozPro.Infrastructure.Voice;

/// <summary>PCM 16-bit audioni WAV faylga o'raydi (xotirada, diskka yozmasdan).</summary>
public static class WavEncoder
{
    public static byte[] FromPcm16(ReadOnlySpan<byte> pcm, int sampleRate = 16000, short channels = 1)
    {
        const short bits = 16;
        var byteRate = sampleRate * channels * bits / 8;
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Yozuv ovoz darajasi (0..1) — jim yozuvni aniqlash uchun.</summary>
    public static double PeakLevel(ReadOnlySpan<byte> pcm16)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            var s = Math.Abs((int)BitConverter.ToInt16(pcm16.Slice(i, 2)));
            if (s > peak) peak = s;
        }
        return peak / 32768.0;
    }
}

/// <summary>
/// Nutqni matnga aylantirish: OpenAI audio transkripsiya API (o'zbek tilini qo'llab-quvvatlaydi).
/// Audio faqat so'rov vaqtida xotirada bo'ladi, diskka yozilmaydi va saqlanmaydi.
/// </summary>
public sealed class OpenAiTranscriber
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _http;

    public OpenAiTranscriber(string apiKey, string? baseUrl = null, string model = "whisper-1", HttpMessageHandler? handler = null, int timeoutSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new AiException("Nutqni aniqlash uchun OpenAI API kaliti kerak (Sozlamalar → AI xizmati).");
        _apiKey = apiKey.Trim();
        _baseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com" : baseUrl.Trim()).TrimEnd('/');
        _model = string.IsNullOrWhiteSpace(model) ? "whisper-1" : model.Trim();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <param name="language">ISO-639-1: "uz", "ru", "en" yoki null (avtomatik).</param>
    public async Task<string> TranscribeAsync(byte[] wav, string? language, CancellationToken ct)
    {
        if (wav.Length <= 44) throw new AiException("Yozuv bo'sh.");
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(_model), "model");
        if (!string.IsNullOrWhiteSpace(language)) form.Add(new StringContent(language), "language");
        form.Add(new StringContent("json"), "response_format");
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/audio/transcriptions") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new AiException("Nutqni aniqlash xizmati javob bermadi.", ex); }
        catch (HttpRequestException ex) { throw new AiException("Nutqni aniqlash xizmatiga ulanib bo'lmadi. Internetni tekshiring.", ex); }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new AiException("OpenAI API kaliti noto'g'ri yoki ruxsat yo'q.");
            if (!resp.IsSuccessStatusCode) throw new AiException($"Nutqni aniqlash xatosi ({(int)resp.StatusCode}).");
            try
            {
                var text = JsonNode.Parse(body)?["text"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(text)) throw new AiException("Nutq aniqlanmadi. Aniqroq va balandroq gapirib ko'ring.");
                return text;
            }
            catch (System.Text.Json.JsonException ex) { throw new AiException("Javobni o'qib bo'lmadi.", ex); }
        }
    }
}
