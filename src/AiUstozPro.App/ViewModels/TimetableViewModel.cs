using System.Collections.ObjectModel;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class TimetableRow
{
    public required TimetableEntry Entry { get; init; }
    public string Day => Entry.DayOfWeek.ToUz();
    public int LessonNumber => Entry.LessonNumber;
    public string Time => $"{Entry.StartTime:HH\\:mm}–{Entry.EndTime:HH\\:mm}";
    public string Group => Entry.Group?.Name ?? "";
    public string Subject => Entry.Subject?.Name ?? "";
    public string Teacher => Entry.Teacher?.FullName ?? "";
    public string Room => Entry.Room ?? "";
    public int Hours => Entry.AcademicHours;
    public string Period => Entry.ValidFrom is null && Entry.ValidTo is null ? "Butun yil"
        : $"{Entry.ValidFrom:dd.MM.yyyy} — {Entry.ValidTo:dd.MM.yyyy}";
}

public sealed class WeekRow
{
    public int LessonNumber { get; init; }
    public string Mon { get; set; } = "";
    public string Tue { get; set; } = "";
    public string Wed { get; set; } = "";
    public string Thu { get; set; } = "";
    public string Fri { get; set; } = "";
    public string Sat { get; set; } = "";
    public string Sun { get; set; } = "";

    public void Append(DayOfWeek d, string text)
    {
        static string J(string a, string b) => a.Length == 0 ? b : a + "\n" + b;
        switch (d)
        {
            case DayOfWeek.Monday: Mon = J(Mon, text); break;
            case DayOfWeek.Tuesday: Tue = J(Tue, text); break;
            case DayOfWeek.Wednesday: Wed = J(Wed, text); break;
            case DayOfWeek.Thursday: Thu = J(Thu, text); break;
            case DayOfWeek.Friday: Fri = J(Fri, text); break;
            case DayOfWeek.Saturday: Sat = J(Sat, text); break;
            case DayOfWeek.Sunday: Sun = J(Sun, text); break;
        }
    }
}

public sealed partial class TimetableViewModel : PageViewModel
{
    public override string Title => "Dars jadvali";
    public bool CanEdit => Can(Permission.EditTimetable);

    public ObservableCollection<TimetableRow> Rows { get; } = new();
    public ObservableCollection<WeekRow> Week { get; } = new();
    public ObservableCollection<Choice<int?>> FilterGroups { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<User> Teachers { get; } = new();
    public List<Choice<DayOfWeek>> Days => Choices.Days;

    [ObservableProperty] private string _calendarInfo = "";
    [ObservableProperty] private bool _hasCalendar;
    [ObservableProperty] private Choice<int?>? _filterGroup;
    [ObservableProperty] private TimetableRow? _selectedRow;

    [ObservableProperty] private int _entryId;
    [ObservableProperty] private Group? _formGroup;
    [ObservableProperty] private Subject? _formSubject;
    [ObservableProperty] private User? _formTeacher;
    [ObservableProperty] private Choice<DayOfWeek>? _formDay;
    [ObservableProperty] private string _formLessonNumber = "1";
    [ObservableProperty] private string _formStart = "08:30";
    [ObservableProperty] private string _formEnd = "09:50";
    [ObservableProperty] private string _formRoom = "";
    [ObservableProperty] private string _formHours = "2";
    [ObservableProperty] private DateTime? _formValidFrom;
    [ObservableProperty] private DateTime? _formValidTo;

    private int _calendarId;

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            var cal = S.Calendar.GetActiveCalendar();
            HasCalendar = cal is not null;
            _calendarId = cal?.Id ?? 0;
            CalendarInfo = cal is null ? "Avval \"O'quv kalendari\" bo'limida o'quv yilini sozlang." : $"O'quv yili: {cal.Name}";

            var keepFilter = FilterGroup?.Value;
            Groups.Clear(); FilterGroups.Clear();
            FilterGroups.Add(new Choice<int?>(null, "Barcha guruhlar"));
            foreach (var g in S.Academic.ListGroups(Session)) { Groups.Add(g); FilterGroups.Add(new Choice<int?>(g.Id, g.Name)); }
            Subjects.Clear();
            foreach (var s in S.Academic.ListSubjects()) Subjects.Add(s);
            Teachers.Clear();
            foreach (var t in S.Auth.ListTeachers()) Teachers.Add(t);
            FilterGroup = FilterGroups.FirstOrDefault(f => f.Value == keepFilter) ?? FilterGroups.First();
            LoadRows();
            if (EntryId == 0) NewEntry();
        });
    }

    partial void OnFilterGroupChanged(Choice<int?>? value) => LoadRows();

    private void LoadRows()
    {
        Rows.Clear(); Week.Clear();
        if (_calendarId == 0) return;
        var entries = S.Calendar.ListTimetable(_calendarId, FilterGroup?.Value);
        foreach (var e in entries) Rows.Add(new TimetableRow { Entry = e });
        var maxLesson = entries.Count == 0 ? 0 : entries.Max(e => e.LessonNumber);
        for (int n = 1; n <= Math.Max(maxLesson, 1); n++)
        {
            var w = new WeekRow { LessonNumber = n };
            foreach (var e in entries.Where(e => e.LessonNumber == n))
                w.Append(e.DayOfWeek, $"{e.Subject?.Name} · {e.Group?.Name}" + (string.IsNullOrWhiteSpace(e.Room) ? "" : $" · {e.Room}"));
            Week.Add(w);
        }
    }

    partial void OnSelectedRowChanged(TimetableRow? value)
    {
        if (value is null) return;
        var e = value.Entry;
        EntryId = e.Id;
        FormGroup = Groups.FirstOrDefault(g => g.Id == e.GroupId);
        FormSubject = Subjects.FirstOrDefault(s => s.Id == e.SubjectId);
        FormTeacher = Teachers.FirstOrDefault(t => t.Id == e.TeacherUserId);
        FormDay = Days.First(d => d.Value == e.DayOfWeek);
        FormLessonNumber = e.LessonNumber.ToString();
        FormStart = e.StartTime.ToString("HH:mm");
        FormEnd = e.EndTime.ToString("HH:mm");
        FormRoom = e.Room ?? "";
        FormHours = e.AcademicHours.ToString();
        FormValidFrom = Ui.ToDateTime(e.ValidFrom);
        FormValidTo = Ui.ToDateTime(e.ValidTo);
    }

    [RelayCommand]
    private void NewEntry()
    {
        SelectedRow = null;
        EntryId = 0;
        FormGroup ??= FilterGroup?.Value is int gid ? Groups.FirstOrDefault(g => g.Id == gid) : Groups.FirstOrDefault();
        FormSubject ??= Subjects.FirstOrDefault();
        FormTeacher = Teachers.FirstOrDefault(t => t.Id == Session.UserId) ?? Teachers.FirstOrDefault();
        FormDay ??= Days.First();
        FormRoom = "";
        FormValidFrom = null; FormValidTo = null;
    }

    private TimetableEntry? BuildEntry()
    {
        if (_calendarId == 0) { Ui.Warn("Avval o'quv yilini sozlang."); return null; }
        if (FormGroup is null || FormSubject is null || FormTeacher is null || FormDay is null) { Ui.Warn("Guruh, fan, o'qituvchi va kunni tanlang."); return null; }
        if (!int.TryParse(FormLessonNumber, out var no)) { Ui.Warn("Dars raqami butun son bo'lishi kerak."); return null; }
        if (!int.TryParse(FormHours, out var hours)) { Ui.Warn("Akademik soat butun son bo'lishi kerak."); return null; }
        if (!Ui.TryParseTime(FormStart, out var start) || !Ui.TryParseTime(FormEnd, out var end)) { Ui.Warn("Vaqtni SS:dd ko'rinishida kiriting, masalan 08:30."); return null; }
        return new TimetableEntry
        {
            Id = EntryId, AcademicCalendarId = _calendarId, GroupId = FormGroup.Id, SubjectId = FormSubject.Id,
            TeacherUserId = FormTeacher.Id, DayOfWeek = FormDay.Value, LessonNumber = no, StartTime = start, EndTime = end,
            Room = FormRoom, AcademicHours = hours, ValidFrom = Ui.ToDateOnly(FormValidFrom), ValidTo = Ui.ToDateOnly(FormValidTo),
        };
    }

    [RelayCommand]
    private void SaveEntry()
    {
        var entry = BuildEntry();
        if (entry is null) return;
        Ui.Run(() =>
        {
            var (saved, conflicts) = S.Calendar.SaveTimetableEntry(Session, entry, allowConflicts: false);
            if (saved is null)
            {
                var msg = "Ziddiyat aniqlandi:\n\n" + string.Join("\n", conflicts.Select(c => "• " + c.Message)) + "\n\nBaribir saqlansinmi?";
                if (!Ui.Confirm(msg, dangerous: true)) return;
                (saved, _) = S.Calendar.SaveTimetableEntry(Session, entry, allowConflicts: true);
            }
            Toast.Show("Jadval saqlandi. Dars sanalarini yangilash uchun \"Dars sanalari\" bo'limida qayta hisoblang.");
            EntryId = 0;
            LoadRows();
            NewEntry();
        });
    }

    [RelayCommand]
    private void DeleteEntry()
    {
        if (SelectedRow is not { } row) return;
        if (!Ui.Confirm($"{row.Day}, {row.LessonNumber}-dars ({row.Subject}, {row.Group}) jadvaldan o'chirilsinmi?\n\nO'tilgan va davomat olingan darslar saqlanib qoladi, faqat kelajakdagi rejalashtirilgan darslar o'chiriladi.", dangerous: true))
            return;
        string msg = "";
        if (Ui.Run(() => msg = S.Calendar.DeleteTimetableEntry(Session, row.Entry.Id)))
        {
            Ui.Info(msg);
            EntryId = 0;
            LoadRows();
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (_calendarId == 0) return;
        var path = Ui.SaveFile("Dars jadvali.xlsx", ReportExporter.SaveFilter);
        if (path is null) return;
        var t = new ReportTable { Title = "Dars jadvali", Landscape = true };
        t.SubtitleLines.Add(CalendarInfo);
        if (FilterGroup?.Value is not null) t.SubtitleLines.Add($"Guruh: {FilterGroup.Text}");
        t.Columns.AddRange(new[] { "Kun", "Dars", "Vaqt", "Guruh", "Fan", "O'qituvchi", "Xona", "Soat", "Amal qilish davri" });
        t.Widths.AddRange(new[] { 1.4f, 0.7f, 1.3f, 1.5f, 2.5f, 2.5f, 0.9f, 0.7f, 2f });
        foreach (var r in Rows)
            t.Rows.Add(new[] { r.Day, r.LessonNumber.ToString(), r.Time, r.Group, r.Subject, r.Teacher, r.Room, r.Hours.ToString(), r.Period });
        var fmt = ReportExporter.FormatFromPath(path);
        if (Ui.Run(() => ReportExporter.Export(t, fmt, path), "Jadval eksport qilindi")) Ui.ShowInFolder(path);
    }

    private static readonly ImportField[] ImportFields =
    {
        new("day", "Kun", true, new[] { "kun", "hafta kuni", "день", "day" }),
        new("no", "Dars raqami", true, new[] { "dars", "dars raqami", "para", "№", "урок" }),
        new("start", "Boshlanishi", true, new[] { "boshlanishi", "boshlanish", "boshlanish vaqti", "начало", "start" }),
        new("end", "Tugashi", true, new[] { "tugashi", "tugash", "tugash vaqti", "конец", "end" }),
        new("group", "Guruh", true, new[] { "guruh", "sinf", "группа", "group" }),
        new("subject", "Fan", true, new[] { "fan", "fan nomi", "предмет", "subject" }),
        new("room", "Xona", false, new[] { "xona", "auditoriya", "кабинет", "room" }),
        new("hours", "Soat", false, new[] { "soat", "akademik soat", "часы", "hours" }),
    };

    private static DayOfWeek? ParseDay(string s)
    {
        var t = ColumnMapper.Normalize(s);
        if (t.Length == 0) return null;
        if (int.TryParse(t, out var n) && n >= 1 && n <= 7) return Choices.WeekOrder[n - 1];
        foreach (var d in Choices.WeekOrder)
        {
            var uz = ColumnMapper.Normalize(d.ToUz());
            if (uz == t || (t.Length >= 2 && uz.StartsWith(t))) return d;
        }
        return null;
    }

    [RelayCommand]
    private void Import()
    {
        if (_calendarId == 0) { Ui.Warn("Avval o'quv yilini sozlang."); return; }
        var groups = Groups.ToList();
        var subjects = Subjects.ToList();
        var dlg = new ImportDialog("Dars jadvalini import qilish",
            "Ustunlar: Kun (Dushanba yoki 1), Dars raqami, Boshlanishi (08:30), Tugashi (09:50), Guruh, Fan, Xona, Soat. Guruh va fan nomlari dasturda mavjud bo'lishi kerak. O'qituvchi — siz.",
            ImportFields,
            (data, map) =>
            {
                var list = new List<PreviewRow>();
                foreach (var f in ImportFields.Where(f => f.Required))
                    if (!map.TryGetValue(f.Key, out var c) || c < 0)
                        return new List<PreviewRow> { new() { RowNumber = 0, IsValid = false, Messages = $"Majburiy ustun tanlanmagan: \"{f.Label}\"." } };
                for (int i = 0; i < data.Rows.Count; i++)
                {
                    string Get(string k) => map.TryGetValue(k, out var c) && c >= 0 ? data.Cell(i, c) : "";
                    var errors = new List<string>();
                    var day = ParseDay(Get("day"));
                    if (day is null) errors.Add($"Kun tanilmadi: \"{Get("day")}\".");
                    if (!int.TryParse(Get("no"), out var no)) errors.Add("Dars raqami noto'g'ri.");
                    if (!Ui.TryParseTime(Get("start"), out var st)) errors.Add("Boshlanish vaqti noto'g'ri.");
                    if (!Ui.TryParseTime(Get("end"), out var en)) errors.Add("Tugash vaqti noto'g'ri.");
                    var g = groups.FirstOrDefault(x => ColumnMapper.Normalize(x.Name) == ColumnMapper.Normalize(Get("group")));
                    if (g is null) errors.Add($"Guruh topilmadi: \"{Get("group")}\".");
                    var s = subjects.FirstOrDefault(x => ColumnMapper.Normalize(x.Name) == ColumnMapper.Normalize(Get("subject")));
                    if (s is null) errors.Add($"Fan topilmadi: \"{Get("subject")}\".");
                    var hours = int.TryParse(Get("hours"), out var h) && h > 0 ? h : 2;
                    TimetableEntry? item = errors.Count > 0 ? null : new TimetableEntry
                    {
                        AcademicCalendarId = _calendarId, GroupId = g!.Id, SubjectId = s!.Id, TeacherUserId = Session.UserId,
                        DayOfWeek = day!.Value, LessonNumber = no, StartTime = st, EndTime = en, Room = Get("room"), AcademicHours = hours,
                    };
                    list.Add(new PreviewRow
                    {
                        RowNumber = i + 2, IsValid = item is not null, Item = item, Messages = string.Join(" ", errors),
                        Values = new[] { Get("day"), Get("no"), Get("start"), Get("end"), Get("group"), Get("subject") },
                    });
                }
                return list;
            }) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        int saved = 0;
        var skipped = new List<string>();
        Ui.Run(() =>
        {
            foreach (var e in dlg.ValidItems.Cast<TimetableEntry>())
            {
                var (ok, conflicts) = S.Calendar.SaveTimetableEntry(Session, e, allowConflicts: false);
                if (ok is null) skipped.Add($"{e.DayOfWeek.ToUz()} {e.LessonNumber}-dars: {conflicts.First().Message}");
                else saved++;
            }
        });
        LoadRows();
        Ui.Info($"{saved} ta jadval yozuvi qo'shildi." + (skipped.Count > 0 ? $"\n\nZiddiyat sababli qo'shilmadi ({skipped.Count}):\n" + string.Join("\n", skipped.Take(15)) : ""));
    }
}
