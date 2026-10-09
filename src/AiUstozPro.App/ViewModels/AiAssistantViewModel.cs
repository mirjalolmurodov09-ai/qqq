using System.Collections.ObjectModel;
using System.Windows;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Ai;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed partial class AiMessageVm : ObservableObject
{
    public required AiMessage Message { get; init; }
    public bool IsAssistant => Message.Role == "assistant";
    public string Who => IsAssistant ? "AI yordamchi" : "Siz";
    public string Time => Message.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private bool _isReviewed;
    public string ReviewText => IsAssistant ? (IsReviewed ? "✔ O'qituvchi tasdiqlagan" : "Tekshirilmagan — AI xato qilishi mumkin") : "";
    partial void OnIsReviewedChanged(bool value) => OnPropertyChanged(nameof(ReviewText));
}

public sealed record TopicChoice(int Id, string Title, int Hours, string Type, string? Outcome)
{
    public override string ToString() => Title;
}

public sealed partial class AiAssistantViewModel : PageViewModel
{
    public override string Title => "AI yordamchi";

    public ObservableCollection<AiConversation> Conversations { get; } = new();
    public ObservableCollection<AiMessageVm> Messages { get; } = new();
    public List<AiTaskTemplate> Templates { get; } = PromptTemplates.All.ToList();
    public ObservableCollection<GroupSubjectPair> Pairs { get; } = new();
    public ObservableCollection<TopicChoice> Topics { get; } = new();
    public List<string> Levels { get; } = new() { "oson", "o'rta", "murakkab" };

    [ObservableProperty] private AiConversation? _selectedConversation;
    [ObservableProperty] private AiTaskTemplate? _template;
    [ObservableProperty] private GroupSubjectPair? _pair;
    [ObservableProperty] private TopicChoice? _topic;
    [ObservableProperty] private string _topicText = "";
    [ObservableProperty] private string _level = "o'rta";
    [ObservableProperty] private string _count = "10";
    [ObservableProperty] private string _extra = "";
    [ObservableProperty] private string _followUp = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _showNewForm = true;
    [ObservableProperty] private string _templateHint = "";
    [ObservableProperty] private bool _needsTopic;
    [ObservableProperty] private bool _needsExtra;
    [ObservableProperty] private bool _isAnalytics;

    private int _calendarId;
    private List<string> _studentNames = new();

    public override void OnActivated()
    {
        Ui.Run(() =>
        {
            IsEnabled = S.Ai.IsEnabled;
            Status = S.Ai.StatusText();
            var cal = S.Calendar.GetActiveCalendar();
            _calendarId = cal?.Id ?? 0;
            var keep = Pair;
            Pairs.Clear();
            if (cal is not null)
                foreach (var p in S.Calendar.ListGroupSubjectPairs(cal.Id)) Pairs.Add(new GroupSubjectPair(p.GroupId, p.GroupName, p.SubjectId, p.SubjectName));
            Pair = Pairs.FirstOrDefault(p => keep is not null && p.GroupId == keep.GroupId && p.SubjectId == keep.SubjectId) ?? Pairs.FirstOrDefault();
            Template ??= Templates.First();
            _studentNames = S.Academic.ListGroups(Session).SelectMany(g => S.Academic.ListStudents(Session, g.Id, true))
                .SelectMany(s => new[] { s.LastName, s.FirstName + " " + s.LastName }).Where(n => n.Trim().Length >= 4).Distinct().ToList();
            LoadConversations(SelectedConversation?.Id);
        });
    }

    private void LoadConversations(int? selectId)
    {
        Conversations.Clear();
        foreach (var c in S.Ai.ListConversations(Session)) Conversations.Add(c);
        var target = Conversations.FirstOrDefault(c => c.Id == selectId);
        if (target is null) { SelectedConversation = null; Messages.Clear(); ShowNewForm = true; }
        else if (!ReferenceEquals(SelectedConversation, target)) SelectedConversation = target;
        else LoadMessages();
    }

    partial void OnSelectedConversationChanged(AiConversation? value)
    {
        ShowNewForm = value is null;
        LoadMessages();
    }

    private void LoadMessages()
    {
        Messages.Clear();
        if (SelectedConversation is null) return;
        Ui.Run(() =>
        {
            foreach (var m in S.Ai.GetMessages(Session, SelectedConversation.Id))
                Messages.Add(new AiMessageVm { Message = m, Content = m.Content, IsReviewed = m.IsReviewed });
        });
    }

    partial void OnTemplateChanged(AiTaskTemplate? value)
    {
        TemplateHint = value?.Hint ?? "";
        NeedsTopic = value?.NeedsTopic == true;
        NeedsExtra = value?.NeedsExtra == true;
        IsAnalytics = value?.Key == "analytics";
    }

    partial void OnPairChanged(GroupSubjectPair? value)
    {
        Topics.Clear();
        if (value is null) return;
        Ui.Run(() =>
        {
            var plan = S.Curriculum.FindPlan(_calendarId, value.GroupId, value.SubjectId);
            if (plan is null) return;
            foreach (var r in S.Curriculum.ListTopics(plan.Id))
                Topics.Add(new TopicChoice(r.Topic.Id, $"{r.Topic.OrderNo}. {r.Topic.Title}", r.Topic.Hours, r.Topic.Type.ToUz(), r.Topic.ExpectedOutcome));
        });
    }

    partial void OnTopicChanged(TopicChoice? value)
    {
        if (value is not null) TopicText = value.Title.Contains(". ") ? value.Title[(value.Title.IndexOf(". ") + 2)..] : value.Title;
    }

    [RelayCommand]
    private void NewConversation()
    {
        SelectedConversation = null;
        ShowNewForm = true;
        Extra = ""; FollowUp = "";
    }

    [RelayCommand]
    private void DeleteConversation()
    {
        if (SelectedConversation is not { } c) return;
        if (!Ui.Confirm($"\"{c.Title}\" suhbati o'chirilsinmi?")) return;
        if (Ui.Run(() => S.Ai.DeleteConversation(Session, c.Id), "Suhbat o'chirildi")) LoadConversations(null);
    }

    [RelayCommand]
    private void InsertAttendance()
    {
        if (Pair is not { } p) { Ui.Warn("Guruh va fanni tanlang."); return; }
        var cal = S.Calendar.GetActiveCalendar();
        if (cal is null) return;
        Ui.Run(() =>
        {
            var to = DateOnly.FromDateTime(DateTime.Today) < cal.EndDate ? DateOnly.FromDateTime(DateTime.Today) : cal.EndDate;
            Extra = S.Reports.AnonymousAttendance(Session, p.GroupId, p.SubjectId, cal.StartDate, to);
        });
    }

    private bool CheckPrivacy(string text)
    {
        var found = Anonymizer.FindNames(text, _studentNames);
        if (found.Count == 0) return true;
        return Ui.Confirm($"Matnda o'quvchi ism-familiyasiga o'xshash so'zlar bor: {string.Join(", ", found.Take(5))}.\n\nShaxsiy ma'lumotlarni tashqi AI xizmatiga yuborish tavsiya etilmaydi. Baribir yuborilsinmi?", dangerous: true);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken token)
    {
        if (Template is not { } t) return;
        if (!S.Ai.IsEnabled) { Ui.Warn("AI yordamchi sozlanmagan. Administrator: Sozlamalar → AI xizmati."); return; }
        if (t.NeedsTopic && string.IsNullOrWhiteSpace(TopicText)) { Ui.Warn("Mavzuni tanlang yoki kiriting."); return; }
        if (t.NeedsExtra && !t.NeedsTopic && string.IsNullOrWhiteSpace(Extra)) { Ui.Warn("Matn maydonini to'ldiring."); return; }
        var ctx = new AiTaskContext
        {
            Subject = Pair?.SubjectName ?? "",
            Course = Pair?.GroupName ?? "",
            Topic = TopicText.Trim(),
            Hours = Topic?.Hours ?? 2,
            LessonType = Topic?.Type ?? "",
            ExpectedOutcome = Topic?.Outcome ?? "",
            Level = Level,
            Count = int.TryParse(Count, out var n) && n > 0 && n <= 50 ? n : 10,
            Extra = Extra,
        };
        var prompt = t.Build(ctx);
        if (!CheckPrivacy(prompt)) return;
        await Run(async () =>
        {
            var r = await S.Ai.AskAsync(Session, null, t.Key, PromptTemplates.MakeTitle(t, ctx), prompt, token);
            LoadConversations(r.Conversation.Id);
        });
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SendFollowUpAsync(CancellationToken token)
    {
        if (SelectedConversation is not { } c || string.IsNullOrWhiteSpace(FollowUp)) return;
        if (!CheckPrivacy(FollowUp)) return;
        var text = FollowUp.Trim();
        await Run(async () =>
        {
            await S.Ai.AskAsync(Session, c.Id, "chat", "", text, token);
            FollowUp = "";
            LoadConversations(c.Id);
        });
    }

    private async Task Run(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException) { Toast.Show("So'rov bekor qilindi", true); }
        catch (AiException ex) { Ui.Warn(ex.Message); }
        catch (Exception ex) when (ex is AiUstozPro.Application.Security.BusinessRuleException or AiUstozPro.Application.Security.AccessDeniedException)
        {
            Ui.Warn(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI so'rovi");
            Ui.Error($"AI so'rovida kutilmagan xato: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            Status = S.Ai.StatusText();
        }
    }

    [RelayCommand]
    private void Copy(AiMessageVm? m)
    {
        if (m is null) return;
        try { Clipboard.SetText(m.Content); Toast.Show("Nusxa olindi"); }
        catch (Exception ex) { Ui.Warn($"Nusxa olib bo'lmadi: {ex.Message}"); }
    }

    [RelayCommand]
    private void Review(AiMessageVm? m)
    {
        if (m is null || !m.IsAssistant) return;
        var dlg = new ReviewDialog(m.Content) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        if (Ui.Run(() =>
        {
            var saved = S.Ai.SaveReviewed(Session, m.Message.Id, dlg.Value);
            m.Content = saved.Content;
            m.IsReviewed = true;
        }, "Javob tasdiqlandi")) { }
    }

    [RelayCommand]
    private void SaveWord(AiMessageVm? m)
    {
        if (m is null || !m.IsAssistant) return;
        if (!m.IsReviewed)
        {
            if (!Ui.Confirm("Bu javob hali tekshirilmagan. Rasmiy hujjatga faqat tekshirilgan matn chiqarilishi kerak. Hozir tekshirib tasdiqlaysizmi?")) return;
            Review(m);
            if (!m.IsReviewed) return;
        }
        var title = SelectedConversation?.Title ?? "AI yordamchi";
        var path = Ui.SaveFile(Sanitize(title) + ".docx", "Word (*.docx)|*.docx");
        if (path is null) return;
        var meta = new[]
        {
            $"AI yordamida tayyorlandi ({SelectedConversation?.Model}), o'qituvchi tekshirdi va tasdiqladi: {Session.FullName}, {DateTime.Now:dd.MM.yyyy}",
        };
        if (Ui.Run(() => DocxWriter.WriteText(title, meta, m.Content, path), "Word hujjati saqlandi")) Ui.OpenPath(path);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 80 ? s[..80] : s;
    }
}
