using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed partial class AttendanceRowVm : ObservableObject
{
    public required Student Student { get; init; }
    public int No { get; init; }
    public string Name => Student.FullName + (Student.IsActive ? "" : " (arxiv)");
    public string Number => Student.StudentNumber ?? "";
    public AttendanceStatus? OriginalStatus { get; init; }
    public string? OriginalNote { get; init; }

    [ObservableProperty] private Choice<AttendanceStatus>? _status;
    [ObservableProperty] private string _note = "";

    public bool IsChanged => OriginalStatus is not null && (Status?.Value != OriginalStatus || (Note ?? "") != (OriginalNote ?? ""));
}

public sealed partial class AttendanceViewModel : PageViewModel
{
    public override string Title => "Davomat";
    public bool CanEdit => Can(Permission.TakeAttendance);
    public List<Choice<AttendanceStatus>> Statuses => Choices.AttendanceStatuses;

    public ObservableCollection<LessonCard> DayLessons { get; } = new();
    public ObservableCollection<AttendanceRowVm> Rows { get; } = new();

    [ObservableProperty] private DateTime? _date = DateTime.Today;
    [ObservableProperty] private LessonCard? _selectedLesson;
    [ObservableProperty] private string _lessonTitle = "";
    [ObservableProperty] private string _lessonTopic = "";
    [ObservableProperty] private string _stats = "";
    [ObservableProperty] private bool _hasLesson;

    private int? _pendingLessonId;

    public override void OnActivated() => LoadDay();

    /// <summary>Bosh sahifadan aniq darsni ochish.</summary>
    public void OpenLesson(int lessonId, DateOnly date)
    {
        _pendingLessonId = lessonId;
        if (Date?.Date != date.ToDateTime(TimeOnly.MinValue)) Date = date.ToDateTime(TimeOnly.MinValue);
        else LoadDay();
    }

    partial void OnDateChanged(DateTime? value) => LoadDay();

    private void LoadDay()
    {
        Ui.Run(() =>
        {
            DayLessons.Clear();
            if (Date is not { } d) return;
            var lessons = S.Calendar.LessonsOnDate(Session, DateOnly.FromDateTime(d));
            var topics = S.Dashboard.TopicsForLessons(lessons.Select(l => l.Id));
            foreach (var l in lessons.Where(l => l.Status != LessonStatus.Cancelled))
                DayLessons.Add(new LessonCard { Lesson = l, Topic = topics.TryGetValue(l.Id, out var t) ? t : null });
            var target = _pendingLessonId is int id ? DayLessons.FirstOrDefault(c => c.Lesson.Id == id) : null;
            _pendingLessonId = null;
            SelectedLesson = target ?? DayLessons.FirstOrDefault();
            if (SelectedLesson is null) { Rows.Clear(); HasLesson = false; LessonTitle = "Bu kunda dars yo'q."; LessonTopic = ""; Stats = ""; }
        });
    }

    partial void OnSelectedLessonChanged(LessonCard? value) => LoadSheet();

    private void LoadSheet()
    {
        Rows.Clear();
        HasLesson = SelectedLesson is not null;
        if (SelectedLesson is not { } card) return;
        Ui.Run(() =>
        {
            var (lesson, sheet) = S.Attendance.GetSheet(Session, card.Lesson.Id);
            LessonTitle = $"{lesson.Date:dd.MM.yyyy}, {lesson.LessonNumber}-dars ({lesson.StartTime:HH\\:mm}) — {lesson.Subject?.Name}, {lesson.Group?.Name}";
            LessonTopic = card.Topic is null ? "Mavzu joylashtirilmagan" : $"Mavzu: {card.Topic}";
            int i = 0;
            foreach (var r in sheet)
            {
                var row = new AttendanceRowVm
                {
                    Student = r.Student, No = ++i, OriginalStatus = r.Status, OriginalNote = r.Note,
                };
                row.Status = r.Status is { } st ? Statuses.First(s => s.Value == st) : null;
                row.Note = r.Note ?? "";
                row.PropertyChanged += (_, _) => UpdateStats();
                Rows.Add(row);
            }
            UpdateStats();
        });
    }

    private void UpdateStats()
    {
        int marked = Rows.Count(r => r.Status is not null);
        int present = Rows.Count(r => r.Status?.Value is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.LeftWithPermission);
        Stats = Rows.Count == 0 ? "Guruhda faol o'quvchi yo'q."
            : $"Belgilangan: {marked} / {Rows.Count}. Darsda: {present}. Kelmagan: {Rows.Count(r => r.Status?.Value is AttendanceStatus.AbsentExcused or AttendanceStatus.AbsentUnexcused)}."
              + (Rows.Any(r => r.IsChanged) ? " Saqlangan yozuvlar o'zgartirildi — saqlashda sabab so'raladi." : "");
    }

    [RelayCommand]
    private void AllPresent()
    {
        var present = Statuses.First(s => s.Value == AttendanceStatus.Present);
        foreach (var r in Rows.Where(r => r.Status is null)) r.Status = present;
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedLesson is not { } card) return;
        var unmarked = Rows.Count(r => r.Status is null);
        if (unmarked > 0 && !Ui.Confirm($"{unmarked} ta o'quvchi belgilanmagan. Ular uchun yozuv yaratilmaydi. Davom etasizmi?")) return;
        string? reason = null;
        if (Rows.Any(r => r.IsChanged))
        {
            reason = Ui.Ask("Davomatni tuzatish", "Avval saqlangan davomat o'zgartirilmoqda. Tuzatish sababini kiriting (audit jurnaliga yoziladi):");
            if (reason is null) return;
        }
        var marks = Rows.Where(r => r.Status is not null).Select(r => new AttendanceMark(r.Student.Id, r.Status!.Value, r.Note)).ToList();
        AttendanceSaveResult? res = null;
        if (Ui.Run(() => res = S.Attendance.Save(Session, card.Lesson.Id, marks, reason)))
        {
            Toast.Show($"Davomat saqlandi: yangi {res!.Added}, tuzatildi {res.Changed}.");
            LoadSheet();
        }
    }

    [RelayCommand] private void Reload() => LoadSheet();
}
