using System.Collections.ObjectModel;
using System.Windows.Threading;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public enum PresentationMode { Topic, Timer, Task, Poll }

public sealed partial class PollOption : ObservableObject
{
    public required string Letter { get; init; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private int _count;
    [ObservableProperty] private double _share;
    [ObservableProperty] private bool _isCorrect;
}

public sealed class MaterialRow
{
    public required TopicMaterial Material { get; init; }
    public string Title => Material.Title;
    public string Kind => Material.IsLink ? "Havola" : "Fayl";
    public string Location => Material.Location;
}

public sealed partial class LessonModeViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime? _endsAt;
    private TimeSpan _remaining;
    private Views.PresentationWindow? _presentation;

    public LessonModeViewModel(MainViewModel main)
    {
        _main = main;
        _timer.Tick += (_, _) => TickTimer();
        foreach (var l in "ABCD") PollOptions.Add(new PollOption { Letter = l.ToString() });
    }

    public override string Title => "Dars rejimi";
    public bool CanEdit => Can(Permission.TakeAttendance);
    public VoiceController Voice => VoiceController.Instance;

    public ObservableCollection<LessonCard> DayLessons { get; } = new();
    public ObservableCollection<CurriculumTopic> Topics { get; } = new();
    public ObservableCollection<MaterialRow> Materials { get; } = new();
    public ObservableCollection<PollOption> PollOptions { get; } = new();

    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private LessonCard? _selectedLesson;
    [ObservableProperty] private bool _hasLesson;
    [ObservableProperty] private string _lessonHeader = "";
    [ObservableProperty] private string _topicTitle = "";
    [ObservableProperty] private string _topicDetails = "";
    [ObservableProperty] private string _attendanceText = "";
    [ObservableProperty] private CurriculumTopic? _selectedTopic;
    [ObservableProperty] private MaterialRow? _selectedMaterial;

    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _homework = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _pollResults = "";

    [ObservableProperty] private string _timerMinutes = "5";
    [ObservableProperty] private string _timerText = "05:00";
    [ObservableProperty] private bool _timerRunning;
    [ObservableProperty] private bool _timerFinished;

    [ObservableProperty] private string _taskText = "";
    [ObservableProperty] private string _pollQuestion = "";
    [ObservableProperty] private bool _showPollResults;
    [ObservableProperty] private PresentationMode _mode = PresentationMode.Topic;
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private bool _voiceEnabled;

    public bool IsTopicMode => Mode == PresentationMode.Topic;
    public bool IsTimerMode => Mode == PresentationMode.Timer;
    public bool IsTaskMode => Mode == PresentationMode.Task;
    public bool IsPollMode => Mode == PresentationMode.Poll;

    partial void OnModeChanged(PresentationMode value)
    {
        OnPropertyChanged(nameof(IsTopicMode)); OnPropertyChanged(nameof(IsTimerMode));
        OnPropertyChanged(nameof(IsTaskMode)); OnPropertyChanged(nameof(IsPollMode));
    }

    public override void OnActivated()
    {
        try { VoiceEnabled = S.Voice.GetSettings().Enabled; } catch { VoiceEnabled = false; }
        LoadDay();
    }

    partial void OnDateChanged(DateTime? value) => LoadDay();

    private void LoadDay()
    {
        Ui.Run(() =>
        {
            var keep = SelectedLesson?.Lesson.Id;
            DayLessons.Clear();
            if (Date is not { } d) return;
            var lessons = S.Calendar.LessonsOnDate(Session, DateOnly.FromDateTime(d)).Where(l => l.Status != LessonStatus.Cancelled).ToList();
            var topics = S.Dashboard.TopicsForLessons(lessons.Select(l => l.Id));
            foreach (var l in lessons) DayLessons.Add(new LessonCard { Lesson = l, Topic = topics.TryGetValue(l.Id, out var t) ? t : null });
            var now = TimeOnly.FromDateTime(DateTime.Now);
            SelectedLesson = DayLessons.FirstOrDefault(c => c.Lesson.Id == keep)
                ?? DayLessons.FirstOrDefault(c => c.Lesson.StartTime <= now && c.Lesson.EndTime >= now)
                ?? DayLessons.FirstOrDefault(c => c.Lesson.StartTime >= now)
                ?? DayLessons.FirstOrDefault();
            if (SelectedLesson is null) { HasLesson = false; LessonHeader = "Bu kunda dars yo'q."; }
        });
    }

    partial void OnSelectedLessonChanged(LessonCard? value) => LoadLesson();

    private void LoadLesson()
    {
        Topics.Clear(); Materials.Clear();
        HasLesson = SelectedLesson is not null;
        if (SelectedLesson is not { } card) return;
        Ui.Run(() =>
        {
            var c = S.Lessons.GetContext(Session, card.Lesson.Id);
            var l = c.Lesson;
            LessonHeader = $"{l.Date:dd.MM.yyyy}, {l.LessonNumber}-dars ({l.StartTime:HH\\:mm}–{l.EndTime:HH\\:mm}) · {l.Subject?.Name} · {l.Group?.Name} · {l.Status.ToUz()}";
            foreach (var t in c.Topics) Topics.Add(t.Topic);
            TopicTitle = c.Topics.Count == 0 ? (l.Subject?.Name ?? "") : string.Join("; ", c.Topics.Select(t => t.Topic.Title));
            TopicDetails = c.Topics.Count == 0 ? "KTR mavzusi joylashtirilmagan." : string.Join("\n", c.Topics.Select(t =>
                $"{t.Topic.OrderNo}. {t.Topic.Title} — {t.Topic.Hours} soat, {t.Topic.Type.ToUz()}" +
                (string.IsNullOrWhiteSpace(t.Topic.ExpectedOutcome) ? "" : $"\nKutilayotgan natija: {t.Topic.ExpectedOutcome}")));
            AttendanceText = c.Marked == 0 ? $"Davomat olinmagan (guruhda {c.Students} o'quvchi)." : $"Darsda: {c.Present}, kelmagan: {c.Absent} (guruhda {c.Students}).";
            Notes = c.Log?.Notes ?? ""; Homework = c.Log?.Homework ?? ""; Summary = c.Log?.Summary ?? ""; PollResults = c.Log?.PollResults ?? "";
            SelectedTopic = Topics.FirstOrDefault();
            LoadMaterials(c);
        });
    }

    private void LoadMaterials(LessonContext? c = null)
    {
        Materials.Clear();
        if (SelectedLesson is null) return;
        c ??= S.Lessons.GetContext(Session, SelectedLesson.Lesson.Id);
        foreach (var t in c.Topics) foreach (var m in t.Materials) Materials.Add(new MaterialRow { Material = m });
    }

    // ---------- Jurnal ----------

    [RelayCommand]
    private void SaveLog()
    {
        if (SelectedLesson is not { } card) return;
        Ui.Run(() => S.Lessons.SaveLog(Session, card.Lesson.Id, Notes, Homework, Summary, PollResults), "Dars jurnali saqlandi");
    }

    [RelayCommand]
    private void MarkConducted()
    {
        if (SelectedLesson is not { } card) return;
        if (Ui.Run(() =>
            {
                S.Lessons.SaveLog(Session, card.Lesson.Id, Notes, Homework, Summary, PollResults);
                S.Calendar.SetConducted(Session, card.Lesson.Id, true);
            }, "Dars o'tildi deb belgilandi"))
            LoadLesson();
    }

    [RelayCommand]
    private void CompleteTopic()
    {
        if (SelectedTopic is not { } t) { Ui.Warn("Mavzu yo'q."); return; }
        if (!Ui.Confirm($"\"{t.Title}\" mavzusi o'tildi deb belgilansinmi? (KTR da qulflanadi)")) return;
        if (Ui.Run(() => S.Curriculum.MarkTopicCompleted(Session, t.Id, SelectedLesson?.Lesson.Date, null), "Mavzu o'tildi deb belgilandi")) LoadLesson();
    }

    [RelayCommand]
    private void GoAttendance()
    {
        if (SelectedLesson is not { } card) return;
        _main.NavigateTo<AttendanceViewModel>()?.OpenLesson(card.Lesson.Id, card.Lesson.Date);
    }

    [RelayCommand]
    private async Task AiSummary()
    {
        if (SelectedLesson is not { } card) return;
        if (!S.Ai.IsEnabled) { Ui.Warn("AI yordamchi sozlanmagan (Sozlamalar → AI xizmati)."); return; }
        var t = PromptTemplates.Get("summary");
        var ctx = new AiTaskContext
        {
            Subject = card.Lesson.Subject?.Name ?? "", Course = card.Lesson.Group?.Name ?? "",
            Topic = TopicTitle, Hours = card.Lesson.AcademicHours, Extra = Notes,
        };
        AiBusy = true;
        try
        {
            var r = await S.Ai.AskAsync(Session, null, t.Key, PromptTemplates.MakeTitle(t, ctx), t.Build(ctx), CancellationToken.None);
            Summary = r.Answer.Content;
            Ui.Info("AI xulosa tayyorladi. Uni o'qib chiqing va kerak bo'lsa tahrirlang — jurnalga siz saqlaganingizdagina yoziladi.");
        }
        catch (AiException ex) { Ui.Warn(ex.Message); }
        catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException) { Ui.Warn(ex.Message); }
        finally { AiBusy = false; }
    }

    [RelayCommand]
    private async Task MicNotes() => await Voice.ToggleAsync(t => Notes = string.IsNullOrWhiteSpace(Notes) ? t : Notes.TrimEnd() + " " + t);

    [RelayCommand]
    private void ExportReport()
    {
        if (SelectedLesson is not { } card) return;
        var l = card.Lesson;
        var path = Ui.SaveFile($"Dars hisoboti {l.Date:yyyy-MM-dd} {l.Group?.Name}.docx", "Word (*.docx)|*.docx|PDF (*.pdf)|*.pdf");
        if (path is null) return;
        if (Ui.Run(() =>
            {
                S.Lessons.SaveLog(Session, l.Id, Notes, Homework, Summary, PollResults);
                ReportExporter.ExportText("Dars hisoboti", new[] { $"Tayyorladi: {Session.FullName}, {DateTime.Now:dd.MM.yyyy HH:mm}" },
                    S.Lessons.ReportText(Session, l.Id), ReportExporter.FormatFromPath(path), path);
            }, "Dars hisoboti saqlandi"))
            Ui.OpenPath(path);
    }

    // ---------- Materiallar ----------

    [RelayCommand]
    private void OpenMaterial(MaterialRow? m)
    {
        m ??= SelectedMaterial;
        if (m is null) return;
        if (!m.Material.IsLink && !System.IO.File.Exists(m.Location)) { Ui.Warn($"Fayl topilmadi: {m.Location}"); return; }
        Ui.OpenPath(m.Location);
    }

    [RelayCommand]
    private void AddFile()
    {
        if (SelectedTopic is not { } t) { Ui.Warn("Material qo'shish uchun darsga KTR mavzusi biriktirilgan bo'lishi kerak."); return; }
        var path = Ui.OpenFile("Barcha fayllar (*.*)|*.*|Taqdimot (*.pptx)|*.pptx|PDF (*.pdf)|*.pdf|Word (*.docx)|*.docx");
        if (path is null) return;
        if (Ui.Run(() => S.Lessons.AddMaterial(Session, t.Id, "", path), "Material qo'shildi")) LoadMaterials();
    }

    [RelayCommand]
    private void AddLink()
    {
        if (SelectedTopic is not { } t) { Ui.Warn("Material qo'shish uchun darsga KTR mavzusi biriktirilgan bo'lishi kerak."); return; }
        var url = Ui.Ask("Havola qo'shish", "Havola (https://…):");
        if (url is null) return;
        var title = Ui.Ask("Havola nomi", "Qisqa nomi (ixtiyoriy):", "", required: false) ?? "";
        if (Ui.Run(() => S.Lessons.AddMaterial(Session, t.Id, title, url), "Havola qo'shildi")) LoadMaterials();
    }

    [RelayCommand]
    private void DeleteMaterial()
    {
        if (SelectedMaterial is not { } m) return;
        if (!Ui.Confirm($"\"{m.Title}\" ro'yxatdan olib tashlansinmi? (Faylning o'zi o'chirilmaydi)")) return;
        if (Ui.Run(() => S.Lessons.DeleteMaterial(Session, m.Material.Id))) LoadMaterials();
    }

    // ---------- Taymer ----------

    [RelayCommand]
    private void SetMinutes(string? minutes)
    {
        if (minutes is not null) TimerMinutes = minutes;
        ResetTimer();
    }

    [RelayCommand]
    private void StartTimer()
    {
        if (!TimerRunning && _remaining <= TimeSpan.Zero) ResetTimer();
        if (_remaining <= TimeSpan.Zero) return;
        _endsAt = DateTime.UtcNow + _remaining;
        TimerRunning = true;
        TimerFinished = false;
        _timer.Start();
        Mode = PresentationMode.Timer;
    }

    [RelayCommand]
    private void PauseTimer()
    {
        if (!TimerRunning) return;
        _timer.Stop();
        TimerRunning = false;
        if (_endsAt is { } e) _remaining = e - DateTime.UtcNow;
    }

    [RelayCommand]
    private void ResetTimer()
    {
        _timer.Stop();
        TimerRunning = false;
        TimerFinished = false;
        _remaining = double.TryParse(TimerMinutes.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0 && m <= 180
            ? TimeSpan.FromMinutes(m) : TimeSpan.FromMinutes(5);
        TimerText = Format(_remaining);
    }

    private void TickTimer()
    {
        if (_endsAt is not { } e) return;
        var left = e - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _timer.Stop();
            TimerRunning = false;
            TimerFinished = true;
            _remaining = TimeSpan.Zero;
            TimerText = "00:00";
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { /* ovoz qurilmasi yo'q */ }
            return;
        }
        TimerText = Format(left);
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    // ---------- Topshiriq va tezkor so'rov ----------

    [RelayCommand] private void ShowTopic() { Mode = PresentationMode.Topic; OpenPresentation(); }
    [RelayCommand] private void ShowTimer() { Mode = PresentationMode.Timer; OpenPresentation(); }
    [RelayCommand] private void ShowTask() { Mode = PresentationMode.Task; OpenPresentation(); }
    [RelayCommand] private void ShowPoll() { Mode = PresentationMode.Poll; OpenPresentation(); }

    [RelayCommand]
    private void OpenPresentation()
    {
        if (_presentation is { IsLoaded: true }) { _presentation.Activate(); return; }
        _presentation = new Views.PresentationWindow { DataContext = this, Owner = Ui.Owner };
        _presentation.Closed += (_, _) => _presentation = null;
        _presentation.Show();
    }

    [RelayCommand]
    private void Vote(PollOption? o)
    {
        if (o is null) return;
        o.Count++;
        RecalcPoll();
    }

    [RelayCommand]
    private void Unvote(PollOption? o)
    {
        if (o is null || o.Count == 0) return;
        o.Count--;
        RecalcPoll();
    }

    [RelayCommand]
    private void ResetPoll()
    {
        foreach (var o in PollOptions) { o.Count = 0; o.Share = 0; }
        ShowPollResults = false;
    }

    private void RecalcPoll()
    {
        var total = PollOptions.Sum(o => o.Count);
        foreach (var o in PollOptions) o.Share = total == 0 ? 0 : (double)o.Count / total;
    }

    [RelayCommand]
    private void SavePoll()
    {
        if (string.IsNullOrWhiteSpace(PollQuestion)) { Ui.Warn("Savol matnini kiriting."); return; }
        var opts = PollOptions.Where(o => !string.IsNullOrWhiteSpace(o.Text)).Select(o => ($"{o.Letter}) {o.Text}", o.Count)).ToList();
        var correct = PollOptions.FirstOrDefault(o => o.IsCorrect && !string.IsNullOrWhiteSpace(o.Text));
        var text = QuickPoll.Format(PollQuestion, opts, correct is null ? null : $"{correct.Letter}) {correct.Text}");
        PollResults = string.IsNullOrWhiteSpace(PollResults) ? text : PollResults.TrimEnd() + "\n\n" + text;
        SaveLog();
    }
}
