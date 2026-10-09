using AiUstozPro.Application.Ai;
using AiUstozPro.Infrastructure.Voice;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiUstozPro.App.Services;

/// <summary>Ovozli kiritish va o'qib berish uchun umumiy boshqaruvchi (sahifalar o'rtasida bitta nusxa).</summary>
public sealed partial class VoiceController : ObservableObject
{
    private static VoiceController? _instance;
    public static VoiceController Instance => _instance ??= new VoiceController();

    private readonly MicrophoneRecorder _recorder = new();
    private readonly Speaker _speaker = new();
    private Action<string>? _target;

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private string _status = "";

    private VoiceController()
    {
        _recorder.AutoStopped += async () => await StopAndTranscribeAsync();
    }

    public VoiceSettings Settings => AppState.Services.Voice.GetSettings();
    public bool Enabled => Settings.Enabled;

    /// <summary>Birinchi bosishda yozishni boshlaydi, ikkinchisida to'xtatib matnga aylantiradi va natijani <paramref name="target"/> ga beradi.</summary>
    public async Task ToggleAsync(Action<string> target)
    {
        if (IsTranscribing) return;
        if (IsRecording) { await StopAndTranscribeAsync(); return; }
        var s = Settings;
        if (!s.Enabled) { Ui.Info("Ovozli rejim o'chirilgan. Sozlamalar → Ovozli yordamchi bo'limida yoqing."); return; }
        if (s.Stt == SttProvider.OpenAI && !AppState.Services.Voice.HasOpenAiKey)
        {
            Ui.Warn("O'zbek nutqini aniqlash uchun OpenAI API kaliti kerak (Sozlamalar → AI xizmati → OpenAI → kalit). Yoki Windows rejimini tanlang (faqat o'rnatilgan tillar).");
            return;
        }
        try
        {
            _speaker.Stop();
            _recorder.Start(s.MaxRecordSeconds);
            _target = target;
            IsRecording = true;
            Status = $"Yozilmoqda… gapiring, tugatgach tugmani yana bosing (ko'pi bilan {s.MaxRecordSeconds} soniya).";
        }
        catch (InvalidOperationException ex)
        {
            Status = "";
            Ui.Warn(ex.Message);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        if (!IsRecording) return;
        IsRecording = false;
        byte[] wav;
        try { wav = _recorder.Stop(); }
        catch (InvalidOperationException ex) { Status = ex.Message; return; }
        IsTranscribing = true;
        Status = "Nutq matnga aylantirilmoqda…";
        try
        {
            var s = Settings;
            string text = s.Stt == SttProvider.OpenAI
                ? await AppState.Services.Voice.TranscribeOpenAiAsync(AppState.RequireSession, wav, CancellationToken.None)
                : await Task.Run(() => WindowsRecognizer.Transcribe(wav, s.Language));
            _target?.Invoke(text);
            Status = "Matn qo'shildi — tekshirib, so'ng yuboring.";
        }
        catch (AiException ex) { Status = ""; Ui.Warn(ex.Message); }
        catch (InvalidOperationException ex) { Status = ""; Ui.Warn(ex.Message); }
        catch (Exception ex) { Status = ""; Log.Error(ex, "Nutqni aniqlash"); Ui.Warn("Nutqni aniqlab bo'lmadi: " + ex.Message); }
        finally
        {
            Array.Clear(wav);
            IsTranscribing = false;
        }
    }

    public void Speak(string text)
    {
        var s = Settings;
        if (!s.Enabled) { Ui.Info("Ovozli rejim o'chirilgan (Sozlamalar → Ovozli yordamchi)."); return; }
        try { _speaker.Speak(text, s); }
        catch (Exception ex) { Ui.Warn("Matnni o'qib bo'lmadi: " + ex.Message); }
    }

    public void StopSpeaking() => _speaker.Stop();
    public void Replay() { try { _speaker.Replay(Settings); } catch (Exception ex) { Ui.Warn(ex.Message); } }
}
