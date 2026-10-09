using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;
using AiUstozPro.Infrastructure.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public enum ReportKind { Attendance, Lessons, Curriculum, Audit }

public sealed partial class ReportsViewModel : PageViewModel
{
    public override string Title => "Hisobotlar";

    public ObservableCollection<Choice<ReportKind>> Kinds { get; } = new();
    public ObservableCollection<Choice<int>> Groups { get; } = new();
    public ObservableCollection<Choice<int?>> Subjects { get; } = new();
    public ObservableCollection<GroupSubjectPair> Pairs { get; } = new();

    [ObservableProperty] private Choice<ReportKind>? _kind;
    [ObservableProperty] private Choice<int>? _group;
    [ObservableProperty] private Choice<int?>? _subject;
    [ObservableProperty] private GroupSubjectPair? _pair;
    [ObservableProperty] private DateTime? _from;
    [ObservableProperty] private DateTime? _to;
    [ObservableProperty] private bool _excel = true;
    [ObservableProperty] private bool _pdf;
    [ObservableProperty] private bool _csv;
    [ObservableProperty] private bool _word;
    [ObservableProperty] private bool _needsGroup;
    [ObservableProperty] private bool _needsPair;
    [ObservableProperty] private bool _needsPeriod;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _lastFile = "";

    private int _calendarId;
    private DateOnly? _calStart, _calEnd;

    public ReportsViewModel()
    {
        Kinds.Add(new Choice<ReportKind>(ReportKind.Attendance, "Davomat jurnali (kunlik / haftalik / oylik)"));
        Kinds.Add(new Choice<ReportKind>(ReportKind.Curriculum, "Kalendar-tematik reja: reja va amaliyot"));
        Kinds.Add(new Choice<ReportKind>(ReportKind.Lessons, "Darslar statistikasi"));
        if (AppState.Session?.Can(Permission.ViewAuditLog) == true) Kinds.Add(new Choice<ReportKind>(ReportKind.Audit, "Audit jurnali"));
    }

    public override void OnActivated()
    {
        Ui.Run(() =>
        {
            var cal = S.Calendar.GetActiveCalendar();
            _calendarId = cal?.Id ?? 0;
            _calStart = cal?.StartDate; _calEnd = cal?.EndDate;
            Groups.Clear();
            foreach (var g in S.Academic.ListGroups(Session)) Groups.Add(new Choice<int>(g.Id, g.Name));
            Subjects.Clear();
            Subjects.Add(new Choice<int?>(null, "Barcha fanlar"));
            foreach (var s in S.Academic.ListSubjects()) Subjects.Add(new Choice<int?>(s.Id, s.Name));
            Pairs.Clear();
            if (cal is not null)
                foreach (var p in S.Calendar.ListGroupSubjectPairs(cal.Id)) Pairs.Add(new GroupSubjectPair(p.GroupId, p.GroupName, p.SubjectId, p.SubjectName));
            Group ??= Groups.FirstOrDefault();
            Subject ??= Subjects.FirstOrDefault();
            Pair ??= Pairs.FirstOrDefault();
            Kind ??= Kinds.First();
            if (From is null) ThisMonth();
        });
    }

    partial void OnKindChanged(Choice<ReportKind>? value)
    {
        var k = value?.Value ?? ReportKind.Attendance;
        NeedsGroup = k == ReportKind.Attendance;
        NeedsPair = k == ReportKind.Curriculum;
        NeedsPeriod = k != ReportKind.Curriculum;
        Description = k switch
        {
            ReportKind.Attendance => "O'quvchilar × dars sanalari jadvali: har bir dars uchun holat belgisi, har bir o'quvchi bo'yicha jami va davomat foizi. Fan tanlanmasa — guruhning barcha fanlari.",
            ReportKind.Curriculum => "KTR mavzulari, ajratilgan soat, oylar kesimida rejadagi sanalar, amalda o'tilgan sana, rejadan ortda qolish va qolgan soatlar.",
            ReportKind.Lessons => "Guruh va fan bo'yicha rejadagi, o'tilgan, bekor qilingan, qo'shimcha va ko'chirilgan darslar soni.",
            ReportKind.Audit => "Kim, qachon, qanday o'zgartirish kiritgani va sababi (faqat administrator uchun).",
            _ => "",
        };
    }

    [RelayCommand]
    private void Today() { From = DateTime.Today; To = DateTime.Today; }

    [RelayCommand]
    private void ThisWeek()
    {
        var d = DateTime.Today;
        var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        From = monday; To = monday.AddDays(6);
    }

    [RelayCommand]
    private void ThisMonth()
    {
        var d = DateTime.Today;
        From = new DateTime(d.Year, d.Month, 1);
        To = From.Value.AddMonths(1).AddDays(-1);
    }

    [RelayCommand]
    private void WholeYear()
    {
        if (_calStart is null) { Ui.Warn("O'quv yili sozlanmagan."); return; }
        From = Ui.ToDateTime(_calStart); To = Ui.ToDateTime(_calEnd);
    }

    [RelayCommand]
    private void Generate()
    {
        if (Kind is null) return;
        var fmt = Pdf ? ExportFormat.Pdf : Csv ? ExportFormat.Csv : Word ? ExportFormat.Word : ExportFormat.Excel;
        var filter = fmt switch
        {
            ExportFormat.Pdf => "PDF (*.pdf)|*.pdf",
            ExportFormat.Csv => "CSV (*.csv)|*.csv",
            ExportFormat.Word => "Word (*.docx)|*.docx",
            _ => "Excel (*.xlsx)|*.xlsx",
        };
        var from = Ui.ToDateOnly(From) ?? DateOnly.FromDateTime(DateTime.Today);
        var to = Ui.ToDateOnly(To) ?? from;
        ReportTable? table = null;
        string name = "";
        if (!Ui.Run(() =>
        {
            switch (Kind.Value)
            {
                case ReportKind.Attendance:
                    if (Group is null) throw new BusinessRuleException("Guruhni tanlang.");
                    table = S.Reports.AttendanceMatrix(Session, Group.Value, Subject?.Value, from, to);
                    name = $"Davomat {Group.Text} {from:dd.MM.yyyy}-{to:dd.MM.yyyy}";
                    break;
                case ReportKind.Curriculum:
                    if (Pair is null) throw new BusinessRuleException("Guruh va fanni tanlang.");
                    var plan = S.Curriculum.FindPlan(_calendarId, Pair.GroupId, Pair.SubjectId) ?? throw new BusinessRuleException("Bu guruh va fan uchun KTR yaratilmagan.");
                    table = S.Reports.Curriculum(Session, plan.Id, DateOnly.FromDateTime(DateTime.Today));
                    name = $"KTR {Pair.SubjectName} {Pair.GroupName}";
                    break;
                case ReportKind.Lessons:
                    table = S.Reports.LessonStatistics(Session, from, to);
                    name = $"Darslar statistikasi {from:dd.MM.yyyy}-{to:dd.MM.yyyy}";
                    break;
                case ReportKind.Audit:
                    table = S.Reports.AuditLog(Session, from, to);
                    name = $"Audit jurnali {from:dd.MM.yyyy}-{to:dd.MM.yyyy}";
                    break;
            }
        })) return;
        if (table is null) return;
        var path = Ui.SaveFile(Sanitize(name) + ReportExporter.Extension(fmt), filter);
        if (path is null) return;
        if (Ui.Run(() => ReportExporter.Export(table, fmt, path), "Hisobot tayyor"))
        {
            LastFile = path;
            Ui.OpenPath(path);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (LastFile.Length > 0) Ui.ShowInFolder(LastFile);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
