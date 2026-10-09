using System.Collections.ObjectModel;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class TopicRowVm
{
    public required TopicRow Row { get; init; }
    public CurriculumTopic T => Row.Topic;
    public int OrderNo => T.OrderNo;
    public string Title => T.Title;
    public int Hours => T.Hours;
    public string Type => T.Type.ToUz();
    public string Dates => string.Join(", ", Row.LessonDates.Select(d => d.ToString("dd.MM")));
    public string Actual => T.ActualDate?.ToString("dd.MM.yyyy") ?? "";
    public string Status => IsBehind ? "Rejadan ortda" : T.Status.ToUz();
    public bool IsLocked => T.IsLocked;
    public bool IsBehind => T.Status != TopicStatus.Completed && Row.LessonDates.Count > 0 && Row.LessonDates.Max() < DateOnly.FromDateTime(DateTime.Today);
    public bool IsNotPlaced => T.Status == TopicStatus.NotPlaced;
    public string Note => string.Join("; ", new[] { T.Note, T.TeacherNote }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class PlanChangeRow
{
    public string Topic { get; init; } = "";
    public string Old { get; init; } = "";
    public string New { get; init; } = "";
}

public sealed partial class CurriculumViewModel : PageViewModel
{
    public override string Title => "Kalendar-tematik reja (KTR)";
    public bool CanEdit => Can(Permission.EditCurriculum);
    public List<Choice<TopicType>> Types => Choices.TopicTypes;

    public ObservableCollection<GroupSubjectPair> Pairs { get; } = new();
    public ObservableCollection<TopicRowVm> Topics { get; } = new();
    public ObservableCollection<PlanChangeRow> Changes { get; } = new();

    [ObservableProperty] private GroupSubjectPair? _selectedPair;
    [ObservableProperty] private TopicRowVm? _selectedTopic;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private bool _hasPairs;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private bool _splitMode = true;
    [ObservableProperty] private bool _oneLessonMode;
    [ObservableProperty] private bool _hasChanges;
    [ObservableProperty] private string _changesSummary = "";

    [ObservableProperty] private int _topicId;
    [ObservableProperty] private string _formOrder = "";
    [ObservableProperty] private string _formTitle = "";
    [ObservableProperty] private string _formHours = "2";
    [ObservableProperty] private Choice<TopicType>? _formType;
    [ObservableProperty] private string _formOutcome = "";
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private string _formTeacherNote = "";

    private int _calendarId;
    private CurriculumPlan? _plan;
    private bool _loadingMode;

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            var cal = S.Calendar.GetActiveCalendar();
            _calendarId = cal?.Id ?? 0;
            var keep = SelectedPair;
            Pairs.Clear();
            if (cal is not null)
                foreach (var p in S.Calendar.ListGroupSubjectPairs(cal.Id))
                    Pairs.Add(new GroupSubjectPair(p.GroupId, p.GroupName, p.SubjectId, p.SubjectName));
            HasPairs = Pairs.Count > 0;
            Info = cal is null ? "Avval o'quv yilini sozlang." : Pairs.Count == 0 ? "Avval dars jadvalini kiriting — KTR guruh va fan juftligi uchun yuritiladi." : "";
            SelectedPair = Pairs.FirstOrDefault(p => keep is not null && p.GroupId == keep.GroupId && p.SubjectId == keep.SubjectId) ?? Pairs.FirstOrDefault();
            if (SelectedPair is null) { _plan = null; HasPlan = false; Topics.Clear(); }
        });
    }

    partial void OnSelectedPairChanged(GroupSubjectPair? value)
    {
        Changes.Clear(); HasChanges = false;
        _plan = value is null ? null : S.Curriculum.FindPlan(_calendarId, value.GroupId, value.SubjectId);
        HasPlan = _plan is not null;
        _loadingMode = true;
        SplitMode = _plan?.PlacementMode != TopicPlacementMode.OneLessonPerTopic;
        OneLessonMode = !SplitMode;
        _loadingMode = false;
        LoadTopics();
        NewTopic();
    }

    partial void OnSplitModeChanged(bool value)
    {
        if (_loadingMode || !value || _plan is null) return;
        Ui.Run(() => S.Curriculum.SetPlacementMode(Session, _plan.Id, TopicPlacementMode.SplitByHours), "Qoida: mavzu soatlari darslarga bo'linadi. Qayta joylashtiring.");
        _plan.PlacementMode = TopicPlacementMode.SplitByHours;
    }

    partial void OnOneLessonModeChanged(bool value)
    {
        if (_loadingMode || !value || _plan is null) return;
        Ui.Run(() => S.Curriculum.SetPlacementMode(Session, _plan.Id, TopicPlacementMode.OneLessonPerTopic), "Qoida: har bir mavzu bitta darsga. Qayta joylashtiring.");
        _plan.PlacementMode = TopicPlacementMode.OneLessonPerTopic;
    }

    private void LoadTopics()
    {
        Topics.Clear();
        if (_plan is null) { Summary = ""; return; }
        foreach (var r in S.Curriculum.ListTopics(_plan.Id)) Topics.Add(new TopicRowVm { Row = r });
        var total = Topics.Sum(t => t.Hours);
        var done = Topics.Where(t => t.T.Status == TopicStatus.Completed).Sum(t => t.Hours);
        Summary = Topics.Count == 0 ? "Mavzular yo'q. Excel/CSV dan import qiling yoki qo'lda qo'shing."
            : $"{Topics.Count} ta mavzu, {total} soat. O'tildi: {done} soat, qolgan: {total - done} soat. Rejadan ortda: {Topics.Count(t => t.IsBehind)}, sana yetmagan: {Topics.Count(t => t.IsNotPlaced)}.";
    }

    [RelayCommand]
    private void CreatePlan()
    {
        if (SelectedPair is not { } p) return;
        if (Ui.Run(() => _plan = S.Curriculum.GetOrCreatePlan(Session, _calendarId, p.GroupId, p.SubjectId), "KTR yaratildi"))
        {
            HasPlan = true;
            LoadTopics();
        }
    }

    partial void OnSelectedTopicChanged(TopicRowVm? value)
    {
        if (value is null) return;
        var t = value.T;
        TopicId = t.Id; FormOrder = t.OrderNo.ToString(); FormTitle = t.Title; FormHours = t.Hours.ToString();
        FormType = Types.First(x => x.Value == t.Type);
        FormOutcome = t.ExpectedOutcome ?? ""; FormNote = t.Note ?? ""; FormTeacherNote = t.TeacherNote ?? "";
    }

    [RelayCommand]
    private void NewTopic()
    {
        SelectedTopic = null;
        TopicId = 0; FormOrder = ""; FormTitle = ""; FormHours = "2"; FormType = Types.First();
        FormOutcome = ""; FormNote = ""; FormTeacherNote = "";
    }

    [RelayCommand]
    private void SaveTopic()
    {
        if (_plan is null) { Ui.Warn("Avval KTR yarating."); return; }
        if (!int.TryParse(FormHours, out var hours)) { Ui.Warn("Soat butun son bo'lishi kerak."); return; }
        int order = 0;
        if (FormOrder.Trim().Length > 0 && !int.TryParse(FormOrder, out order)) { Ui.Warn("Tartib raqami butun son bo'lishi kerak."); return; }
        if (Ui.Run(() => S.Curriculum.SaveTopic(Session, new CurriculumTopic
            {
                Id = TopicId, CurriculumPlanId = _plan.Id, OrderNo = order, Title = FormTitle, Hours = hours,
                Type = FormType?.Value ?? TopicType.Theory, ExpectedOutcome = NullIf(FormOutcome), Note = NullIf(FormNote), TeacherNote = NullIf(FormTeacherNote),
            }), "Mavzu saqlandi. Sanalarni yangilash uchun \"Joylashtirish\" ni bosing."))
        {
            LoadTopics();
            NewTopic();
        }
    }

    private static string? NullIf(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [RelayCommand]
    private void DeleteTopic()
    {
        if (SelectedTopic is not { } t) return;
        if (!Ui.Confirm($"\"{t.OrderNo}. {t.Title}\" mavzusi o'chirilsinmi?", dangerous: true)) return;
        if (Ui.Run(() => S.Curriculum.DeleteTopic(Session, t.T.Id), "Mavzu o'chirildi")) { LoadTopics(); NewTopic(); }
    }

    [RelayCommand]
    private void Import()
    {
        if (_plan is null) { Ui.Warn("Avval KTR yarating."); return; }
        var dlg = new ImportDialog("KTR mavzularini import qilish",
            "Fayldagi ustunlar: Tartib raqami, Mavzu nomi, Soat (majburiy), Mashg'ulot turi (Nazariy/Amaliy/Laboratoriya/Nazorat), Kutilayotgan natija, Izoh. Ustunlarni pastda moslashtirishingiz mumkin.",
            TopicImport.Fields,
            (data, map) => TopicImport.Validate(data, map).Select(r => new PreviewRow
            {
                RowNumber = r.RowNumber, IsValid = r.IsValid, Item = r.Item,
                Messages = string.Join(" ", r.Errors.Concat(r.Warnings)),
                Values = r.Item is { } t ? new[] { t.OrderNo.ToString(), t.Title, t.Hours.ToString(), t.Type.ToUz(), t.ExpectedOutcome ?? "", t.Note ?? "" } : Array.Empty<string>(),
            }).ToList(),
            optionText: "Mavjud o'tilmagan mavzularni almashtirish (o'tilgan va qulflangan mavzular saqlanadi)", optionDefault: Topics.Count == 0) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        var replace = dlg.OptionBox?.IsChecked == true;
        int n = 0;
        if (Ui.Run(() => n = S.Curriculum.ImportTopics(Session, _plan.Id, dlg.ValidItems.Cast<CurriculumTopic>(), replace)))
        {
            LoadTopics();
            if (Ui.Confirm($"{n} ta mavzu import qilindi. Endi ularni dars sanalariga joylashtiraymi?")) PreviewPlacement();
        }
    }

    [RelayCommand]
    private void PreviewPlacement()
    {
        if (_plan is null) return;
        Ui.Run(() =>
        {
            var pv = S.Curriculum.PreviewPlacement(_plan.Id);
            Changes.Clear();
            foreach (var c in pv.Changes)
                Changes.Add(new PlanChangeRow { Topic = $"{c.OrderNo}. {c.Title}", Old = c.OldDate?.ToString("dd.MM.yyyy") ?? "—", New = c.NewDate?.ToString("dd.MM.yyyy") ?? "sana yetmadi" });
            var r = pv.Result;
            var notPlaced = r.NotPlaced.Count();
            ChangesSummary = $"Mavjud darslar: {r.AvailableLessonCount} ({r.AvailableHours} soat), kerak: {r.RequiredHours} soat. "
                + $"Sanasi o'zgaradigan mavzular: {pv.Changes.Count}. Bekor qilingan darslar: {pv.CancelledLessons}. "
                + (notPlaced > 0 ? $"DIQQAT: {notPlaced} ta mavzuga dars yetmaydi. " : "")
                + (r.UnusedLessonCount > 0 ? $"Bo'sh qoladigan darslar: {r.UnusedLessonCount}." : "");
            if (r.AvailableLessonCount == 0) ChangesSummary = "Bu guruh va fan uchun dars sanalari yo'q. Avval \"Dars sanalari\" bo'limida hisoblang.";
            HasChanges = true;
        });
    }

    [RelayCommand]
    private void ApplyPlacement()
    {
        if (_plan is null) return;
        if (!HasChanges) PreviewPlacement();
        if (!Ui.Confirm($"{ChangesSummary}\n\nMavzular shu sanalarga joylashtirilsinmi? O'tilgan (qulflangan) mavzular o'zgarmaydi.")) return;
        if (Ui.Run(() => S.Curriculum.ApplyPlacement(Session, _plan.Id), "Mavzular sanalarga joylashtirildi"))
        {
            Changes.Clear(); HasChanges = false;
            LoadTopics();
        }
    }

    [RelayCommand]
    private void MarkCompleted()
    {
        if (SelectedTopic is not { } t) { Ui.Warn("Mavzuni tanlang."); return; }
        var note = Ui.Ask("Mavzu o'tildi", $"\"{t.Title}\" o'tildi deb belgilanadi va qulflanadi. Izoh (ixtiyoriy):", "", required: false);
        if (note is null) return;
        if (Ui.Run(() => S.Curriculum.MarkTopicCompleted(Session, t.T.Id, null, note), "Mavzu o'tildi deb belgilandi")) LoadTopics();
    }

    [RelayCommand]
    private void Unlock()
    {
        if (SelectedTopic is not { } t) return;
        var reason = Ui.Ask("Qulfni ochish", "Mavzu qayta \"rejalashtirilgan\" holatiga o'tadi. Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Curriculum.UnlockTopic(Session, t.T.Id, reason), "Qulf ochildi")) LoadTopics();
    }

    [RelayCommand]
    private void Extend()
    {
        if (SelectedTopic is not { } t) { Ui.Warn("Mavzuni tanlang."); return; }
        var h = Ui.Ask("Mavzuni keyingi darsga davom ettirish", "Mavzu tugallanmadi. Qancha akademik soat qo'shilsin?", "2");
        if (h is null) return;
        if (!int.TryParse(h, out var hours)) { Ui.Warn("Butun son kiriting."); return; }
        var reason = Ui.Ask("Sabab", "Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Curriculum.ExtendTopic(Session, t.T.Id, hours, reason)))
        {
            LoadTopics();
            PreviewPlacement();
            Ui.Info("Mavzu uzaytirildi. O'ng tomonda keyingi mavzular sanalari qanday o'zgarishi ko'rsatildi — \"Qo'llash\" tugmasi bilan saqlang.");
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (_plan is null || SelectedPair is not { } p) return;
        var path = Ui.SaveFile($"KTR {p.SubjectName} {p.GroupName}.xlsx", "Excel (*.xlsx)|*.xlsx|PDF (*.pdf)|*.pdf|CSV (*.csv)|*.csv");
        if (path is null) return;
        var fmt = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Pdf
                : path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Csv : ExportFormat.Excel;
        if (Ui.Run(() => ReportExporter.Export(S.Reports.Curriculum(Session, _plan.Id, DateOnly.FromDateTime(DateTime.Today)), fmt, path), "KTR eksport qilindi"))
            Ui.OpenPath(path);
    }
}
