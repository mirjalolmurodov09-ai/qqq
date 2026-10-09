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

public sealed partial class GroupsViewModel : PageViewModel
{
    public override string Title => "Guruhlar va o'quvchilar";

    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<Student> Students { get; } = new();
    private List<Student> _allStudents = new();

    [ObservableProperty] private Group? _selectedGroup;
    [ObservableProperty] private Student? _selectedStudent;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showInactive;
    [ObservableProperty] private bool _showArchivedGroups;
    [ObservableProperty] private string _studentsCountText = "";

    // Guruh formasi
    [ObservableProperty] private int _groupId;
    [ObservableProperty] private string _groupName = "";
    [ObservableProperty] private string _groupCourse = "";

    // O'quvchi formasi
    [ObservableProperty] private int _studentId;
    [ObservableProperty] private string _lastName = "";
    [ObservableProperty] private string _firstName = "";
    [ObservableProperty] private string _middleName = "";
    [ObservableProperty] private string _studentNumber = "";
    [ObservableProperty] private string _studentFormTitle = "Yangi o'quvchi";

    public bool CanEdit => Can(Permission.EditGroupsAndStudents);

    public override void OnActivated() => LoadGroups(SelectedGroup?.Id);

    private void LoadGroups(int? selectId)
    {
        Ui.Run(() =>
        {
            Groups.Clear();
            foreach (var g in S.Academic.ListGroups(Session, ShowArchivedGroups)) Groups.Add(g);
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == selectId) ?? Groups.FirstOrDefault();
            if (SelectedGroup is null) { Students.Clear(); _allStudents.Clear(); NewGroup(); }
        });
    }

    partial void OnShowArchivedGroupsChanged(bool value) => LoadGroups(SelectedGroup?.Id);

    partial void OnSelectedGroupChanged(Group? value)
    {
        if (value is not null)
        {
            GroupId = value.Id;
            GroupName = value.Name;
            GroupCourse = value.Course ?? "";
        }
        LoadStudents();
        NewStudent();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnShowInactiveChanged(bool value) => LoadStudents();

    partial void OnSelectedStudentChanged(Student? value)
    {
        if (value is null) return;
        StudentId = value.Id;
        LastName = value.LastName;
        FirstName = value.FirstName;
        MiddleName = value.MiddleName ?? "";
        StudentNumber = value.StudentNumber ?? "";
        StudentFormTitle = "O'quvchini tahrirlash";
    }

    private void LoadStudents()
    {
        _allStudents = SelectedGroup is null ? new() : S.Academic.ListStudents(Session, SelectedGroup.Id, ShowInactive);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = (Search ?? "").Trim().ToLowerInvariant();
        Students.Clear();
        foreach (var s in _allStudents.Where(s => q.Length == 0 || s.FullName.ToLowerInvariant().Contains(q) || (s.StudentNumber ?? "").ToLowerInvariant().Contains(q)))
            Students.Add(s);
        StudentsCountText = $"Ko'rsatilgan: {Students.Count} ta o'quvchi";
    }

    [RelayCommand]
    private void NewGroup()
    {
        GroupId = 0; GroupName = ""; GroupCourse = "";
    }

    [RelayCommand]
    private void SaveGroup()
    {
        Group? saved = null;
        if (Ui.Run(() => saved = S.Academic.SaveGroup(Session, new Group { Id = GroupId, Name = GroupName, Course = GroupCourse, IsArchived = GroupId != 0 && SelectedGroup?.IsArchived == true }), "Guruh saqlandi"))
            LoadGroups(saved?.Id);
    }

    [RelayCommand]
    private void ArchiveGroup()
    {
        if (SelectedGroup is not { } g) return;
        var archive = !g.IsArchived;
        if (!Ui.Confirm(archive ? $"\"{g.Name}\" guruhini arxivlaysizmi? Ma'lumotlar o'chirilmaydi." : $"\"{g.Name}\" guruhini arxivdan chiqarasizmi?")) return;
        if (Ui.Run(() => S.Academic.SaveGroup(Session, new Group { Id = g.Id, Name = g.Name, Course = g.Course, Note = g.Note, IsArchived = archive }),
                archive ? "Guruh arxivlandi" : "Guruh arxivdan chiqarildi"))
            LoadGroups(null);
    }

    [RelayCommand]
    private void NewStudent()
    {
        SelectedStudent = null;
        StudentId = 0; LastName = ""; FirstName = ""; MiddleName = ""; StudentNumber = "";
        StudentFormTitle = "Yangi o'quvchi";
    }

    [RelayCommand]
    private void SaveStudent()
    {
        if (SelectedGroup is null) { Ui.Warn("Avval guruh tanlang yoki yarating."); return; }
        if (StudentId == 0 && S.Academic.HasSameNameInGroup(SelectedGroup.Id, LastName, FirstName, MiddleName)
            && !Ui.Confirm("Bu guruhda xuddi shunday F.I.Sh. li o'quvchi bor. Bu boshqa o'quvchimi? (Ha — yangi o'quvchi sifatida qo'shiladi)"))
            return;
        var isActive = StudentId == 0 || SelectedStudent?.IsActive != false;
        if (Ui.Run(() => S.Academic.SaveStudent(Session, new Student
            {
                Id = StudentId, LastName = LastName, FirstName = FirstName, MiddleName = MiddleName,
                StudentNumber = StudentNumber, GroupId = SelectedGroup.Id, IsActive = isActive,
            }), "O'quvchi saqlandi"))
        {
            LoadStudents();
            NewStudent();
        }
    }

    [RelayCommand]
    private void ToggleStudentActive()
    {
        if (SelectedStudent is not { } s) return;
        var activate = !s.IsActive;
        if (!activate && !Ui.Confirm($"{s.FullName} arxivlansinmi? Davomat tarixi saqlanib qoladi.")) return;
        if (Ui.Run(() => S.Academic.SetStudentActive(Session, s.Id, activate), activate ? "O'quvchi faollashtirildi" : "O'quvchi arxivlandi"))
            LoadStudents();
    }

    [RelayCommand]
    private void ImportStudents()
    {
        if (SelectedGroup is not { } group) { Ui.Warn("Avval guruh tanlang."); return; }
        var (numbers, names) = S.Academic.GetDuplicateKeys(group.Id);
        var dlg = new ImportDialog($"O'quvchilarni import qilish — {group.Name}",
            "Faylning birinchi qatorida ustun sarlavhalari bo'lsin: Familiya, Ism, Otasining ismi, O'quvchi raqami. Bir xil F.I.Sh. li o'quvchilar birlashtirilmaydi — faqat ogohlantiriladi.",
            StudentImport.Fields,
            (data, map) => StudentImport.Validate(data, map, numbers, names).Select(r => new PreviewRow
            {
                RowNumber = r.RowNumber, IsValid = r.IsValid, Item = r.Item,
                Messages = string.Join(" ", r.Errors.Concat(r.Warnings)),
                Values = r.Item is { } s ? new[] { s.LastName, s.FirstName, s.MiddleName ?? "", s.StudentNumber ?? "" } : Array.Empty<string>(),
            }).ToList()) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        var items = dlg.ValidItems.Cast<Student>().ToList();
        int n = 0;
        if (Ui.Run(() => n = S.Academic.ImportStudents(Session, group.Id, items)))
        {
            Toast.Show($"{n} ta o'quvchi import qilindi");
            LoadStudents();
        }
    }

    [RelayCommand]
    private void ExportStudents()
    {
        if (SelectedGroup is not { } group) return;
        var path = Ui.SaveFile($"{group.Name} o'quvchilar.xlsx", "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv");
        if (path is null) return;
        var t = new ReportTable { Title = "O'quvchilar ro'yxati" };
        t.SubtitleLines.Add($"Guruh: {group.Name}");
        t.Columns.AddRange(new[] { "№", "Familiya", "Ism", "Otasining ismi", "O'quvchi raqami", "Holat" });
        int i = 0;
        foreach (var s in _allStudents)
            t.Rows.Add(new[] { (++i).ToString(), s.LastName, s.FirstName, s.MiddleName ?? "", s.StudentNumber ?? "", s.IsActive ? "Faol" : "Arxiv" });
        var fmt = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ExportFormat.Csv : ExportFormat.Excel;
        if (Ui.Run(() => ReportExporter.Export(t, fmt, path), "Ro'yxat eksport qilindi")) Ui.ShowInFolder(path);
    }

    [RelayCommand]
    private void ExportStudentHistory()
    {
        if (SelectedStudent is not { } s) { Ui.Warn("O'quvchini tanlang."); return; }
        var path = Ui.SaveFile($"{s.FullName} davomat.xlsx", ReportExporter.SaveFilter);
        if (path is null) return;
        var fmt = ReportExporter.FormatFromPath(path);
        if (Ui.Run(() => ReportExporter.Export(S.Reports.StudentHistory(Session, s.Id), fmt, path), "Davomat tarixi eksport qilindi"))
            Ui.OpenPath(path);
    }
}
