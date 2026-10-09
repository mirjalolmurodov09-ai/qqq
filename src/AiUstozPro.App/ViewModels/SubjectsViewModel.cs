using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed partial class SubjectsViewModel : PageViewModel
{
    public override string Title => "Fanlar";
    public ObservableCollection<Subject> Subjects { get; } = new();

    [ObservableProperty] private Subject? _selected;
    [ObservableProperty] private int _subjectId;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _code = "";

    public bool CanEdit => Can(Permission.EditTimetable);

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            Subjects.Clear();
            foreach (var s in S.Academic.ListSubjects()) Subjects.Add(s);
        });
    }

    partial void OnSelectedChanged(Subject? value)
    {
        if (value is null) return;
        SubjectId = value.Id; Name = value.Name; Code = value.Code ?? "";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null; SubjectId = 0; Name = ""; Code = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (Ui.Run(() => S.Academic.SaveSubject(Session, new Subject { Id = SubjectId, Name = Name, Code = Code }), "Fan saqlandi"))
        {
            Load();
            New();
        }
    }
}
