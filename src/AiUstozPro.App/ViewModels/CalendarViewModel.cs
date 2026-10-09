using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Calendar;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class ExceptionRow
{
    public required CalendarException Item { get; init; }
    public string Kind => Item.Kind.ToUz();
    public string Dates => Item.EndDate > Item.StartDate ? $"{Item.StartDate:dd.MM.yyyy} — {Item.EndDate:dd.MM.yyyy}" : $"{Item.StartDate:dd.MM.yyyy} ({Item.StartDate.DayOfWeek.ToUz()})";
    public string Title => Item.Title;
    public string WorksAs => Item.WorksAsDayOfWeek is { } d ? $"{d.ToUz()} jadvali" : "";
    public string Scope { get; init; } = "Hammasi";
    public string Confirmed => Item.IsConfirmed ? "Ha" : "Yo'q";
}

public sealed partial class CalendarViewModel : PageViewModel
{
    public override string Title => "O'quv kalendari";
    public bool CanEdit => Can(Permission.EditCalendar);
    public string VariableNotice => UzbekistanHolidays.VariableNotice;

    public List<Choice<CalendarExceptionKind>> Kinds => Choices.ExceptionKinds;
    public List<Choice<DayOfWeek>> Days => Choices.Days;
    public ObservableCollection<Choice<int?>> GroupChoices { get; } = new();
    public ObservableCollection<Choice<int?>> SubjectChoices { get; } = new();

    public ObservableCollection<AcademicTerm> Terms { get; } = new();
    public ObservableCollection<ExceptionRow> Exceptions { get; } = new();

    [ObservableProperty] private int _calendarId;
    [ObservableProperty] private string _calendarName = "";
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private bool _mon = true;
    [ObservableProperty] private bool _tue = true;
    [ObservableProperty] private bool _wed = true;
    [ObservableProperty] private bool _thu = true;
    [ObservableProperty] private bool _fri = true;
    [ObservableProperty] private bool _sat;
    [ObservableProperty] private bool _sun;
    [ObservableProperty] private string _calendarInfo = "";
    [ObservableProperty] private bool _hasCalendar;

    [ObservableProperty] private string _termName = "";
    [ObservableProperty] private DateTime? _termStart;
    [ObservableProperty] private DateTime? _termEnd;
    [ObservableProperty] private AcademicTerm? _selectedTerm;

    [ObservableProperty] private ExceptionRow? _selectedException;
    [ObservableProperty] private int _exceptionId;
    [ObservableProperty] private Choice<CalendarExceptionKind>? _exKind;
    [ObservableProperty] private DateTime? _exStart;
    [ObservableProperty] private DateTime? _exEnd;
    [ObservableProperty] private Choice<DayOfWeek>? _exWorksAs;
    [ObservableProperty] private string _exTitle = "";
    [ObservableProperty] private Choice<int?>? _exGroup;
    [ObservableProperty] private Choice<int?>? _exSubject;
    [ObservableProperty] private bool _exConfirmed = true;
    [ObservableProperty] private string _exSource = "";
    [ObservableProperty] private bool _exIsVacation;
    [ObservableProperty] private bool _exIsTransfer;

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            GroupChoices.Clear();
            GroupChoices.Add(new Choice<int?>(null, "Barcha guruhlar"));
            foreach (var g in S.Academic.ListGroups(Session)) GroupChoices.Add(new Choice<int?>(g.Id, g.Name));
            SubjectChoices.Clear();
            SubjectChoices.Add(new Choice<int?>(null, "Barcha fanlar"));
            foreach (var s in S.Academic.ListSubjects()) SubjectChoices.Add(new Choice<int?>(s.Id, s.Name));

            var cal = S.Calendar.GetActiveCalendar();
            HasCalendar = cal is not null;
            if (cal is null)
            {
                var y = DateTime.Today.Month >= 8 ? DateTime.Today.Year : DateTime.Today.Year - 1;
                CalendarId = 0;
                CalendarName = $"{y}–{y + 1} o'quv yili";
                StartDate = new DateTime(y, 9, 2);
                EndDate = new DateTime(y + 1, 6, 30);
                CalendarInfo = "O'quv yili hali saqlanmagan. Sanalarni tekshirib, \"Saqlash\" tugmasini bosing.";
                Terms.Clear(); Exceptions.Clear();
            }
            else
            {
                CalendarId = cal.Id;
                CalendarName = cal.Name;
                StartDate = Ui.ToDateTime(cal.StartDate);
                EndDate = Ui.ToDateTime(cal.EndDate);
                Mon = cal.IsWorkingDay(DayOfWeek.Monday); Tue = cal.IsWorkingDay(DayOfWeek.Tuesday);
                Wed = cal.IsWorkingDay(DayOfWeek.Wednesday); Thu = cal.IsWorkingDay(DayOfWeek.Thursday);
                Fri = cal.IsWorkingDay(DayOfWeek.Friday); Sat = cal.IsWorkingDay(DayOfWeek.Saturday);
                Sun = cal.IsWorkingDay(DayOfWeek.Sunday);
                CalendarInfo = $"Faol o'quv yili: {cal.Name}, {cal.StartDate:dd.MM.yyyy} — {cal.EndDate:dd.MM.yyyy} ({cal.EndDate.DayNumber - cal.StartDate.DayNumber + 1} kun).";
                LoadTerms();
                LoadExceptions();
            }
            NewException();
        });
    }

    private void LoadTerms()
    {
        Terms.Clear();
        foreach (var t in S.Calendar.ListTerms(CalendarId)) Terms.Add(t);
    }

    private void LoadExceptions()
    {
        Exceptions.Clear();
        foreach (var x in S.Calendar.ListExceptions(CalendarId))
        {
            var scope = new List<string>();
            if (x.GroupId is int g) scope.Add(GroupChoices.FirstOrDefault(c => c.Value == g)?.Text ?? $"guruh #{g}");
            if (x.SubjectId is int s) scope.Add(SubjectChoices.FirstOrDefault(c => c.Value == s)?.Text ?? $"fan #{s}");
            Exceptions.Add(new ExceptionRow { Item = x, Scope = scope.Count == 0 ? "Hammasi" : string.Join(", ", scope) });
        }
    }

    [RelayCommand]
    private void SaveCalendar()
    {
        if (StartDate is null || EndDate is null) { Ui.Warn("Boshlanish va tugash sanalarini tanlang."); return; }
        var mask = 0;
        if (Mon) mask |= 1 << (int)DayOfWeek.Monday;
        if (Tue) mask |= 1 << (int)DayOfWeek.Tuesday;
        if (Wed) mask |= 1 << (int)DayOfWeek.Wednesday;
        if (Thu) mask |= 1 << (int)DayOfWeek.Thursday;
        if (Fri) mask |= 1 << (int)DayOfWeek.Friday;
        if (Sat) mask |= 1 << (int)DayOfWeek.Saturday;
        if (Sun) mask |= 1 << (int)DayOfWeek.Sunday;
        var wasNew = CalendarId == 0;
        if (Ui.Run(() => S.Calendar.SaveCalendar(Session, new AcademicCalendar
            {
                Id = CalendarId, Name = CalendarName, StartDate = DateOnly.FromDateTime(StartDate.Value),
                EndDate = DateOnly.FromDateTime(EndDate.Value), WorkingDaysMask = mask,
            }), "O'quv yili saqlandi"))
        {
            Load();
            if (wasNew && CalendarId != 0 && Ui.Confirm("Sanasi qonun bilan belgilangan bayramlarni (Yangi yil, 8-mart, Navro'z, 9-may, 1-sentabr, 1-oktabr, 8-dekabr) qo'shaymi?"))
                AddFixedHolidays();
            else if (!wasNew)
                Ui.Info("Kalendar o'zgardi. Dars sanalarini yangilash uchun \"Dars sanalari\" bo'limida qayta hisoblang — o'zgarishlarni oldindan ko'rasiz.");
        }
    }

    [RelayCommand]
    private void AddTerm()
    {
        if (CalendarId == 0) { Ui.Warn("Avval o'quv yilini saqlang."); return; }
        if (TermStart is null || TermEnd is null) { Ui.Warn("Davr sanalarini tanlang."); return; }
        if (Ui.Run(() => S.Calendar.SaveTerm(Session, new AcademicTerm
            {
                AcademicCalendarId = CalendarId, Name = TermName,
                StartDate = DateOnly.FromDateTime(TermStart.Value), EndDate = DateOnly.FromDateTime(TermEnd.Value),
            }), "Davr qo'shildi"))
        {
            TermName = ""; TermStart = null; TermEnd = null;
            LoadTerms();
        }
    }

    [RelayCommand]
    private void DeleteTerm()
    {
        if (SelectedTerm is not { } t) return;
        if (!Ui.Confirm($"\"{t.Name}\" davri o'chirilsinmi?")) return;
        if (Ui.Run(() => S.Calendar.DeleteTerm(Session, t.Id), "Davr o'chirildi")) LoadTerms();
    }

    partial void OnExKindChanged(Choice<CalendarExceptionKind>? value)
    {
        ExIsVacation = value?.Value == CalendarExceptionKind.Vacation;
        ExIsTransfer = value?.Value == CalendarExceptionKind.TransferredWorkday;
    }

    partial void OnSelectedExceptionChanged(ExceptionRow? value)
    {
        if (value is null) return;
        var x = value.Item;
        ExceptionId = x.Id;
        ExKind = Kinds.First(k => k.Value == x.Kind);
        ExStart = Ui.ToDateTime(x.StartDate);
        ExEnd = Ui.ToDateTime(x.EndDate);
        ExWorksAs = x.WorksAsDayOfWeek is { } d ? Days.First(c => c.Value == d) : null;
        ExTitle = x.Title;
        ExGroup = GroupChoices.FirstOrDefault(c => c.Value == x.GroupId) ?? GroupChoices.FirstOrDefault();
        ExSubject = SubjectChoices.FirstOrDefault(c => c.Value == x.SubjectId) ?? SubjectChoices.FirstOrDefault();
        ExConfirmed = x.IsConfirmed;
        ExSource = x.Source ?? "";
    }

    [RelayCommand]
    private void NewException()
    {
        SelectedException = null;
        ExceptionId = 0;
        ExKind = Kinds.First();
        ExStart = null; ExEnd = null; ExWorksAs = null; ExTitle = "";
        ExGroup = GroupChoices.FirstOrDefault(); ExSubject = SubjectChoices.FirstOrDefault();
        ExConfirmed = true; ExSource = "";
    }

    [RelayCommand]
    private void SaveException()
    {
        if (CalendarId == 0) { Ui.Warn("Avval o'quv yilini saqlang."); return; }
        if (ExKind is null || ExStart is null) { Ui.Warn("Turi va sanasini tanlang."); return; }
        var start = DateOnly.FromDateTime(ExStart.Value);
        var end = ExIsVacation && ExEnd is { } e ? DateOnly.FromDateTime(e) : start;
        if (Ui.Run(() => S.Calendar.SaveException(Session, new CalendarException
            {
                Id = ExceptionId, AcademicCalendarId = CalendarId, Kind = ExKind.Value, StartDate = start, EndDate = end,
                WorksAsDayOfWeek = ExIsTransfer ? ExWorksAs?.Value : null, Title = ExTitle,
                GroupId = ExGroup?.Value, SubjectId = ExSubject?.Value, IsConfirmed = ExConfirmed,
                Source = string.IsNullOrWhiteSpace(ExSource) ? null : ExSource.Trim(),
            }), "Istisno saqlandi"))
        {
            LoadExceptions();
            NewException();
        }
    }

    [RelayCommand]
    private void DeleteException()
    {
        if (SelectedException is not { } row) return;
        if (!Ui.Confirm($"\"{row.Kind}: {row.Dates}\" o'chirilsinmi? Dars sanalariga ta'siri qayta hisoblashda ko'rsatiladi.")) return;
        if (Ui.Run(() => S.Calendar.DeleteException(Session, row.Item.Id), "Istisno o'chirildi"))
        {
            LoadExceptions();
            NewException();
        }
    }

    [RelayCommand]
    private void AddFixedHolidays()
    {
        if (CalendarId == 0) { Ui.Warn("Avval o'quv yilini saqlang."); return; }
        int n = 0;
        if (Ui.Run(() => n = S.Calendar.AddFixedHolidays(Session, CalendarId)))
        {
            LoadExceptions();
            Ui.Info(n == 0 ? "Barcha qat'iy bayramlar allaqachon kiritilgan." : $"{n} ta bayram qo'shildi.\n\n{UzbekistanHolidays.VariableNotice}");
        }
    }
}
