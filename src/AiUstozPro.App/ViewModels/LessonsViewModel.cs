using System.Collections.ObjectModel;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed record GroupSubjectPair(int GroupId, string GroupName, int SubjectId, string SubjectName)
{
    public override string ToString() => $"{GroupName} — {SubjectName}";
}

public sealed class LessonRow
{
    public required LessonOccurrence Lesson { get; init; }
    public string Date => Lesson.Date.ToString("dd.MM.yyyy");
    public string Day => Lesson.Date.DayOfWeek.ToUz();
    public int Number => Lesson.LessonNumber;
    public string Time => $"{Lesson.StartTime:HH\\:mm}–{Lesson.EndTime:HH\\:mm}";
    public string Status => Lesson.Status.ToUz();
    public string Origin => Lesson.Origin == LessonOrigin.Moved && Lesson.OriginalDate is { } o ? $"Ko'chirilgan ({o:dd.MM})" : Lesson.Origin.ToUz();
    public string? Topic { get; init; }
    public string Note => Lesson.Note ?? "";
    public int Hours => Lesson.AcademicHours;
    public bool IsCancelled => Lesson.Status == LessonStatus.Cancelled;
}

public sealed class PreviewLessonRow
{
    public string Date { get; init; } = "";
    public string Day { get; init; } = "";
    public string Number { get; init; } = "";
    public string Action { get; init; } = "";
    public string Note { get; init; } = "";
    public int Sort { get; init; }
}

public sealed partial class LessonsViewModel : PageViewModel
{
    public override string Title => "Dars sanalari";
    public bool CanEdit => Can(Permission.EditCalendar);

    public ObservableCollection<GroupSubjectPair> Pairs { get; } = new();
    public ObservableCollection<LessonRow> Lessons { get; } = new();
    public ObservableCollection<PreviewLessonRow> Preview { get; } = new();
    public ObservableCollection<Choice<int>> Months { get; } = new();

    [ObservableProperty] private GroupSubjectPair? _selectedPair;
    [ObservableProperty] private LessonRow? _selectedLesson;
    [ObservableProperty] private Choice<int>? _selectedMonth;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _previewSummary = "";
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _hasCalendar;
    [ObservableProperty] private string _info = "";

    private int _calendarId;
    private List<LessonRow> _all = new();

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            var cal = S.Calendar.GetActiveCalendar();
            HasCalendar = cal is not null;
            _calendarId = cal?.Id ?? 0;
            var keep = SelectedPair;
            Pairs.Clear();
            if (cal is null) { Info = "Avval o'quv yilini sozlang."; return; }
            foreach (var p in S.Calendar.ListGroupSubjectPairs(cal.Id))
                Pairs.Add(new GroupSubjectPair(p.GroupId, p.GroupName, p.SubjectId, p.SubjectName));
            Info = Pairs.Count == 0
                ? "Dars jadvali hali kiritilmagan. \"Dars jadvali\" bo'limida guruh va fan uchun haftalik darslarni kiriting."
                : "Guruh va fanni tanlang, so'ng \"Hisoblash\" — dastur bayram, ta'til va istisnolarni hisobga olib sanalarni tayyorlaydi. Saqlashdan oldin natijani ko'rib chiqasiz.";
            Months.Clear();
            Months.Add(new Choice<int>(0, "Barcha oylar"));
            for (var m = new DateOnly(cal.StartDate.Year, cal.StartDate.Month, 1); m <= cal.EndDate; m = m.AddMonths(1))
                Months.Add(new Choice<int>(m.Year * 100 + m.Month, $"{ReportService.MonthUz(m.Month)} {m.Year}"));
            SelectedMonth = Months.First();
            SelectedPair = Pairs.FirstOrDefault(p => keep is not null && p.GroupId == keep.GroupId && p.SubjectId == keep.SubjectId) ?? Pairs.FirstOrDefault();
        });
    }

    partial void OnSelectedPairChanged(GroupSubjectPair? value)
    {
        Preview.Clear();
        HasPreview = false;
        LoadLessons();
    }

    partial void OnSelectedMonthChanged(Choice<int>? value) => ApplyMonth();

    private void LoadLessons()
    {
        _all.Clear();
        if (SelectedPair is { } p)
        {
            var lessons = S.Calendar.ListLessons(p.GroupId, p.SubjectId);
            var topics = S.Dashboard.TopicsForLessons(lessons.Select(l => l.Id));
            _all = lessons.Select(l => new LessonRow { Lesson = l, Topic = topics.TryGetValue(l.Id, out var t) ? t : null }).ToList();
        }
        ApplyMonth();
        var active = _all.Where(r => !r.IsCancelled).ToList();
        Summary = _all.Count == 0 ? "Dars sanalari hali hisoblanmagan."
            : $"Jami: {active.Count} ta dars ({active.Sum(r => r.Hours)} akademik soat), o'tildi: {_all.Count(r => r.Lesson.Status == LessonStatus.Conducted)}, bekor qilingan: {_all.Count(r => r.IsCancelled)}.";
    }

    private void ApplyMonth()
    {
        Lessons.Clear();
        var key = SelectedMonth?.Value ?? 0;
        foreach (var r in _all.Where(r => key == 0 || r.Lesson.Date.Year * 100 + r.Lesson.Date.Month == key)) Lessons.Add(r);
    }

    [RelayCommand]
    private void Calculate()
    {
        if (SelectedPair is not { } p) { Ui.Warn("Guruh va fanni tanlang."); return; }
        Ui.Run(() =>
        {
            var pv = S.Calendar.PreviewLessons(_calendarId, p.GroupId, p.SubjectId);
            var rec = pv.Reconcile;
            var rows = new List<PreviewLessonRow>();
            var addKeys = rec.ToAdd.Select(a => (a.TimetableEntryId, a.Date)).ToHashSet();
            foreach (var g in pv.AllDates)
            {
                string action, note = "";
                if (g.IsSkipped) { action = "Dars yo'q"; note = g.SkipReason ?? ""; }
                else if (addKeys.Contains((g.TimetableEntryId, g.Date))) action = "Qo'shiladi";
                else action = "Mavjud";
                if (g.ActualDayOfWeek != g.ScheduleDayOfWeek) note = $"{g.ScheduleDayOfWeek.ToUz()} jadvali bo'yicha (ko'chirilgan ish kuni)";
                rows.Add(new PreviewLessonRow { Date = g.Date.ToString("dd.MM.yyyy"), Day = g.ActualDayOfWeek.ToUz(), Number = g.LessonNumber.ToString(), Action = action, Note = note, Sort = g.Date.DayNumber * 100 + g.LessonNumber });
            }
            foreach (var r in rec.ToRemove)
                rows.Add(new PreviewLessonRow { Date = r.Date.ToString("dd.MM.yyyy"), Day = r.Date.DayOfWeek.ToUz(), Action = "O'chiriladi", Note = "Yangi kalendar bo'yicha bu kunda dars yo'q (davomat va tasdiq yo'q)", Sort = r.Date.DayNumber * 100 });
            foreach (var (l, msg) in rec.Conflicts)
                rows.Add(new PreviewLessonRow { Date = l.Date.ToString("dd.MM.yyyy"), Day = l.Date.DayOfWeek.ToUz(), Action = "Ziddiyat — saqlanadi", Note = msg, Sort = l.Date.DayNumber * 100 });
            Preview.Clear();
            foreach (var r in rows.OrderBy(r => r.Sort)) Preview.Add(r);
            var skipped = pv.AllDates.Count(d => d.IsSkipped);
            PreviewSummary = $"Qo'shiladi: {rec.ToAdd.Count}, o'chiriladi: {rec.ToRemove.Count}, o'zgarishsiz: {rec.Kept.Count}, bayram/ta'til sababli o'tilmaydi: {skipped}, ziddiyat: {rec.Conflicts.Count}.";
            HasPreview = true;
        });
    }

    [RelayCommand]
    private void Apply()
    {
        if (SelectedPair is not { } p) return;
        if (!HasPreview) Calculate();
        var msg = $"{PreviewSummary}\n\nO'zgarishlar saqlansinmi? Davomat olingan, o'tilgan, tasdiqlangan, qo'lda qo'shilgan yoki ko'chirilgan darslar o'zgartirilmaydi.";
        if (!Ui.Confirm(msg)) return;
        Ui.Run(() =>
        {
            var rec = S.Calendar.ApplyLessons(Session, _calendarId, p.GroupId, p.SubjectId);
            Toast.Show($"Saqlandi: +{rec.ToAdd.Count}, −{rec.ToRemove.Count}. KTR bo'lsa, mavzularni qayta joylashtiring.");
            Preview.Clear();
            HasPreview = false;
            LoadLessons();
        });
    }

    private LessonRow? Require()
    {
        if (SelectedLesson is null) Ui.Warn("Ro'yxatdan darsni tanlang.");
        return SelectedLesson;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Require() is not { } r) return;
        var reason = Ui.Ask("Darsni bekor qilish", $"{r.Date}, {r.Number}-dars bekor qilinadi. Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Calendar.CancelLesson(Session, r.Lesson.Id, reason), "Dars bekor qilindi. KTR mavzularini qayta joylashtirish tavsiya etiladi."))
            LoadLessons();
    }

    [RelayCommand]
    private void Restore()
    {
        if (Require() is not { } r) return;
        if (Ui.Run(() => S.Calendar.RestoreLesson(Session, r.Lesson.Id), "Dars tiklandi")) LoadLessons();
    }

    [RelayCommand]
    private void Move()
    {
        if (Require() is not { } r) return;
        var l = r.Lesson;
        var dlg = new LessonEditDialog("Darsni boshqa kunga ko'chirish", false, l.Date, l.LessonNumber, l.StartTime, l.EndTime, l.Room, l.AcademicHours) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        if (Ui.Run(() => S.Calendar.MoveLesson(Session, l.Id, dlg.Date, dlg.LessonNumber, dlg.Start, dlg.End, dlg.Reason), "Dars ko'chirildi"))
            LoadLessons();
    }

    [RelayCommand]
    private void AddExtra()
    {
        if (SelectedPair is not { } p) { Ui.Warn("Guruh va fanni tanlang."); return; }
        var basis = SelectedLesson?.Lesson;
        var dlg = new LessonEditDialog("Qo'shimcha dars", true, DateOnly.FromDateTime(DateTime.Today), basis?.LessonNumber ?? 1,
            basis?.StartTime ?? new TimeOnly(8, 30), basis?.EndTime ?? new TimeOnly(9, 50), basis?.Room, basis?.AcademicHours ?? 2) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        if (Ui.Run(() => S.Calendar.AddExtraLesson(Session, p.GroupId, p.SubjectId, dlg.Date, dlg.LessonNumber, dlg.Start, dlg.End, dlg.Room, dlg.Hours, dlg.Reason), "Qo'shimcha dars qo'shildi"))
            LoadLessons();
    }

    [RelayCommand]
    private void ToggleConducted()
    {
        if (Require() is not { } r) return;
        var conducted = r.Lesson.Status != LessonStatus.Conducted;
        if (Ui.Run(() => S.Calendar.SetConducted(Session, r.Lesson.Id, conducted), conducted ? "Dars o'tildi deb belgilandi" : "Belgi olib tashlandi"))
            LoadLessons();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Require() is not { } r) return;
        var reason = Ui.Ask("Darsni o'chirish", $"{r.Date}, {r.Number}-dars butunlay o'chiriladi. Odatda \"Bekor qilish\" to'g'riroq. Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Calendar.DeleteLesson(Session, r.Lesson.Id, reason), "Dars o'chirildi")) LoadLessons();
    }

    [RelayCommand]
    private void Export()
    {
        if (SelectedPair is not { } p) return;
        var path = Ui.SaveFile($"Dars sanalari {p.GroupName} {p.SubjectName}.xlsx", "Excel (*.xlsx)|*.xlsx|PDF (*.pdf)|*.pdf|CSV (*.csv)|*.csv");
        if (path is null) return;
        var t = new ReportTable { Title = "Dars sanalari", Landscape = true };
        t.SubtitleLines.Add($"Guruh: {p.GroupName}");
        t.SubtitleLines.Add($"Fan: {p.SubjectName}");
        t.Columns.AddRange(new[] { "№", "Sana", "Kun", "Dars", "Vaqt", "Soat", "Holat", "Turi", "Mavzu", "Izoh" });
        t.Widths.AddRange(new[] { 0.6f, 1.3f, 1.3f, 0.7f, 1.3f, 0.6f, 1.5f, 1.5f, 4f, 2.5f });
        int i = 0;
        foreach (var r in Lessons)
            t.Rows.Add(new[] { (++i).ToString(), r.Date, r.Day, r.Number.ToString(), r.Time, r.Hours.ToString(), r.Status, r.Origin, r.Topic ?? "", r.Note });
        t.FooterLines.Add(Summary);
        var fmt = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Pdf
                : path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Csv : ExportFormat.Excel;
        if (Ui.Run(() => ReportExporter.Export(t, fmt, path), "Eksport qilindi")) Ui.ShowInFolder(path);
    }
}
