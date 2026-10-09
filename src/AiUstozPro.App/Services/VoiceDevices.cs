using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using AiUstozPro.Infrastructure.Voice;
using NAudio.Wave;

namespace AiUstozPro.App.Services;

/// <summary>
/// Mikrofondan yozish: faqat foydalanuvchi tugmani bosganda boshlanadi, belgilangan vaqtdan oshmaydi,
/// audio faqat xotirada saqlanadi va transkripsiyadan so'ng yo'qotiladi.
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    private WaveInEvent? _wave;
    private MemoryStream? _buffer;
    private System.Windows.Threading.DispatcherTimer? _limitTimer;

    public static bool IsAvailable
    {
        get
        {
            try { return WaveInEvent.DeviceCount > 0; }
            catch { return false; }
        }
    }

    public bool IsRecording => _wave is not null;
    public event Action? AutoStopped;

    public void Start(int maxSeconds)
    {
        if (IsRecording) return;
        if (!IsAvailable) throw new InvalidOperationException("Mikrofon topilmadi yoki unga ruxsat berilmagan. Windows sozlamalari → Maxfiylik → Mikrofon bo'limini tekshiring. Matn orqali yozishingiz mumkin.");
        _buffer = new MemoryStream();
        _wave = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        _wave.DataAvailable += (_, e) => _buffer?.Write(e.Buffer, 0, e.BytesRecorded);
        try
        {
            _wave.StartRecording();
        }
        catch (Exception ex)
        {
            Cleanup();
            throw new InvalidOperationException("Mikrofonni ishga tushirib bo'lmadi: " + ex.Message + ". Matnli rejimdan foydalaning.", ex);
        }
        _limitTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(maxSeconds) };
        _limitTimer.Tick += (_, _) => { _limitTimer?.Stop(); AutoStopped?.Invoke(); };
        _limitTimer.Start();
    }

    /// <summary>Yozuvni to'xtatadi va WAV (16 kHz, mono) qaytaradi.</summary>
    public byte[] Stop()
    {
        if (_wave is null || _buffer is null) return Array.Empty<byte>();
        try { _wave.StopRecording(); } catch { /* qurilma uzilgan bo'lishi mumkin */ }
        var pcm = _buffer.ToArray();
        Cleanup();
        if (pcm.Length < 16000) throw new InvalidOperationException("Yozuv juda qisqa. Tugmani bosib, gapirib, so'ng yana bosing.");
        if (WavEncoder.PeakLevel(pcm) < 0.01) throw new InvalidOperationException("Ovoz eshitilmadi. Mikrofon ulanganini va ovoz darajasini tekshiring.");
        var wav = WavEncoder.FromPcm16(pcm);
        Array.Clear(pcm);
        return wav;
    }

    private void Cleanup()
    {
        _limitTimer?.Stop();
        _limitTimer = null;
        _wave?.Dispose();
        _wave = null;
        _buffer?.Dispose();
        _buffer = null;
    }

    public void Dispose() => Cleanup();
}

/// <summary>Matnni ovozda o'qish (Windows nutq sintezatori). O'zbek ovozi o'rnatilmagan bo'lishi mumkin.</summary>
public sealed class Speaker : IDisposable
{
    private SpeechSynthesizer? _synth;
    private string _last = "";

    private SpeechSynthesizer Synth => _synth ??= CreateSynth();

    private static SpeechSynthesizer CreateSynth()
    {
        var s = new SpeechSynthesizer();
        s.SetOutputToDefaultAudioDevice();
        return s;
    }

    public static List<string> InstalledVoices()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            return s.GetInstalledVoices().Where(v => v.Enabled)
                .Select(v => $"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.Name})").ToList();
        }
        catch { return new List<string>(); }
    }

    public bool IsSpeaking => _synth?.State == SynthesizerState.Speaking;

    public void Speak(string text, VoiceSettings settings)
    {
        Stop();
        _last = text;
        var s = Synth;
        s.Volume = Math.Clamp(settings.Volume, 0, 100);
        s.Rate = Math.Clamp(settings.Rate, -10, 10);
        if (!string.IsNullOrWhiteSpace(settings.TtsVoice))
        {
            var name = settings.TtsVoice!.Split(" (")[0];
            try { s.SelectVoice(name); } catch { /* ovoz o'chirilgan bo'lsa — standart ovoz */ }
        }
        s.SpeakAsync(CleanForSpeech(text));
    }

    public void Replay(VoiceSettings settings)
    {
        if (_last.Length > 0) Speak(_last, settings);
    }

    public void Stop()
    {
        try { _synth?.SpeakAsyncCancelAll(); } catch { /* e'tiborsiz */ }
    }

    /// <summary>Markdown belgilari va kod bloklarini o'qishdan oldin olib tashlaydi.</summary>
    public static string CleanForSpeech(string text)
    {
        var t = Regex.Replace(text ?? "", "```[\\s\\S]*?```", " Kod namunasi ekranda. ");
        t = Regex.Replace(t, "[#*_`>|]", " ");
        t = Regex.Replace(t, "^\\s*-\\s+", "", RegexOptions.Multiline);
        return Regex.Replace(t, "[ \\t]+", " ").Trim();
    }

    public void Dispose() => _synth?.Dispose();
}

/// <summary>Windows o'rnatilgan nutqni aniqlash (internetsiz). Odatda faqat ingliz/rus tillari mavjud.</summary>
public static class WindowsRecognizer
{
    public static List<CultureInfo> InstalledCultures()
    {
        try { return SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture).ToList(); }
        catch { return new List<CultureInfo>(); }
    }

    public static string Transcribe(byte[] wav, string language)
    {
        var cultures = InstalledCultures();
        if (cultures.Count == 0) throw new InvalidOperationException("Windows'da nutqni aniqlash moduli o'rnatilmagan. OpenAI rejimidan foydalaning yoki matn bilan yozing.");
        var culture = cultures.FirstOrDefault(c => c.TwoLetterISOLanguageName == language)
            ?? throw new InvalidOperationException($"Windows'da \"{language}\" tili uchun nutqni aniqlash yo'q. Mavjud: {string.Join(", ", cultures.Select(c => c.Name))}. O'zbek tili uchun OpenAI rejimini tanlang.");
        using var engine = new SpeechRecognitionEngine(culture);
        engine.LoadGrammar(new DictationGrammar());
        using var ms = new MemoryStream(wav);
        engine.SetInputToWaveStream(ms);
        var parts = new List<string>();
        RecognitionResult? r;
        while ((r = engine.Recognize(TimeSpan.FromSeconds(5))) is not null) parts.Add(r.Text);
        var text = string.Join(" ", parts).Trim();
        if (text.Length == 0) throw new InvalidOperationException("Nutq aniqlanmadi.");
        return text;
    }
}
