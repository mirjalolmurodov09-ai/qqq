using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class LessonCard
{
    public required LessonOccurrence Lesson { get; init; }
    public string Time => $"{Lesson.StartTime:HH\\:mm}–{Lesson.EndTime:HH\\:mm}";
    public string Header => $"{Lesson.LessonNumber}-dars · {Lesson.Subject?.Name}";
    public string Details => $"{Lesson.Group?.Name}" + (string.IsNullOrWhiteSpace(Lesson.Room) ? "" : $" · {Lesson.Room}-xona") + $" · {Lesson.Status.ToUz()}";
    public string? Topic { get; init; }
}

public sealed class SetupStep
{
    public required string Text { get; init; }
    public bool Done { get; init; }
    public string Mark => Done ? "✔" : "○";
}

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    public DashboardViewModel(MainViewModel main) => _main = main;

    public override string Title => "Bosh sahifa";

    public ObservableCollection<LessonCard> TodayLessons { get; } = new();
    public ObservableCollection<SetupStep> SetupSteps { get; } = new();

    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _nextLessonText = "";
    [ObservableProperty] private string _calendarText = "";
    [ObservableProperty] private string _attendanceText = "—";
    [ObservableProperty] private string _studentsText = "0";
    [ObservableProperty] private string _lessonsText = "0";
    [ObservableProperty] private string _topicsText = "0";
    [ObservableProperty] private bool _setupIncomplete;
    [ObservableProperty] private LessonCard? _selectedLesson;

    public override void OnActivated() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        Ui.Run(() =>
        {
            var now = DateTime.Now;
            var today = DateOnly.FromDateTime(now);
            Greeting = now.Hour < 12 ? $"Xayrli tong, {Session.FullName}!" : now.Hour < 18 ? $"Xayrli kun, {Session.FullName}!" : $"Xayrli kech, {Session.FullName}!";

            var lessons = S.Calendar.LessonsOnDate(Session, today);
            var topics = S.Dashboard.TopicsForLessons(lessons.Select(l => l.Id));
            TodayLessons.Clear();
            foreach (var l in lessons)
                TodayLessons.Add(new LessonCard { Lesson = l, Topic = topics.TryGetValue(l.Id, out var t) ? t : null });

            var next = S.Calendar.NextLesson(Session, now);
            NextLessonText = next is null
                ? "Rejalashtirilgan dars yo'q."
                : $"{next.Date:dd.MM.yyyy} ({next.Date.DayOfWeek.ToUz()}), {next.StartTime:HH\\:mm} — {next.Subject?.Name}, {next.Group?.Name}";

            var cal = S.Calendar.GetActiveCalendar();
            CalendarText = cal is null ? "O'quv yili sozlanmagan" : $"{cal.Name}: {cal.StartDate:dd.MM.yyyy} — {cal.EndDate:dd.MM.yyyy}";

            var st = S.Dashboard.GetStats(Session, today);
            AttendanceText = st.AttendancePercent30Days is double p ? $"{p:0.#}%" : "—";
            StudentsText = $"{st.Students}";
            LessonsText = $"{st.ConductedLessons} / {st.ConductedLessons + st.PlannedLessons}";
            TopicsText = $"{st.CompletedTopics} / {st.Topics}";

            SetupSteps.Clear();
            SetupSteps.Add(new SetupStep { Text = "O'quv yili va bayramlarni sozlang (O'quv kalendari)", Done = st.HasCalendar });
            SetupSteps.Add(new SetupStep { Text = "Guruh yarating va o'quvchilarni kiriting yoki import qiling", Done = st.Groups > 0 && st.Students > 0 });
            SetupSteps.Add(new SetupStep { Text = "Fanlarni qo'shing", Done = st.Subjects > 0 });
            SetupSteps.Add(new SetupStep { Text = "Dars jadvalini kiriting", Done = st.TimetableEntries > 0 });
            SetupSteps.Add(new SetupStep { Text = "Dars sanalarini avtomatik hisoblang (Dars sanalari)", Done = st.PlannedLessons + st.ConductedLessons > 0 });
            SetupSteps.Add(new SetupStep { Text = "KTR mavzularini import qilib, sanalarga joylashtiring", Done = st.Topics > 0 });
            SetupIncomplete = SetupSteps.Any(s => !s.Done);
        });
    }

    [RelayCommand]
    private void TakeAttendance(LessonCard? card)
    {
        card ??= SelectedLesson ?? TodayLessons.FirstOrDefault();
        var page = _main.NavigateTo<AttendanceViewModel>();
        if (page is not null && card is not null) page.OpenLesson(card.Lesson.Id, card.Lesson.Date);
    }

    [RelayCommand] private void GoCalendar() => _main.NavigateTo<CalendarViewModel>();
    [RelayCommand] private void GoGroups() => _main.NavigateTo<GroupsViewModel>();
    [RelayCommand] private void GoTimetable() => _main.NavigateTo<TimetableViewModel>();
    [RelayCommand] private void GoLessons() => _main.NavigateTo<LessonsViewModel>();
    [RelayCommand] private void GoCurriculum() => _main.NavigateTo<CurriculumViewModel>();
    [RelayCommand] private void GoReports() => _main.NavigateTo<ReportsViewModel>();
}
