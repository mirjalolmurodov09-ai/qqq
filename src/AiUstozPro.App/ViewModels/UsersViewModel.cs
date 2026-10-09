using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class UserRow
{
    public required User User { get; init; }
    public string Login => User.Login;
    public string FullName => User.FullName;
    public string Role => User.Role.ToUz();
    public string State => User.IsBlocked ? "Bloklangan" : User.LockedUntilUtc > DateTime.UtcNow ? "Vaqtincha qulflangan" : "Faol";
    public string LastLogin => User.LastLoginUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}

public sealed partial class GroupCheck : ObservableObject
{
    public required Group Group { get; init; }
    [ObservableProperty] private bool _isChecked;
}

public sealed partial class UsersViewModel : PageViewModel
{
    public override string Title => "Foydalanuvchilar";
    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<GroupCheck> GroupChecks { get; } = new();
    public List<Choice<UserRole>> Roles => Choices.Roles;

    [ObservableProperty] private UserRow? _selected;
    [ObservableProperty] private bool _isObserverSelected;
    [ObservableProperty] private string _newLogin = "";
    [ObservableProperty] private string _newFullName = "";
    [ObservableProperty] private Choice<UserRole>? _newRole;

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            var keep = Selected?.User.Id;
            Users.Clear();
            foreach (var u in S.Auth.ListUsers(Session)) Users.Add(new UserRow { User = u });
            NewRole ??= Roles.First(r => r.Value == UserRole.Teacher);
            Selected = Users.FirstOrDefault(u => u.User.Id == keep);
        });
    }

    partial void OnSelectedChanged(UserRow? value)
    {
        IsObserverSelected = value?.User.Role == UserRole.Observer;
        GroupChecks.Clear();
        if (!IsObserverSelected || value is null) return;
        var allowed = S.Auth.GetObserverGroups(value.User.Id).ToHashSet();
        foreach (var g in S.Academic.ListGroups(Session)) GroupChecks.Add(new GroupCheck { Group = g, IsChecked = allowed.Contains(g.Id) });
    }

    [RelayCommand]
    private void Create()
    {
        if (NewRole is null) return;
        var pass = Ui.Ask("Boshlang'ich parol", $"\"{NewLogin}\" uchun parol (kamida 8 belgi, harf va raqam). Foydalanuvchi keyin uni Sozlamalarda o'zgartiradi:");
        if (pass is null) return;
        if (Ui.Run(() => S.Auth.CreateUser(Session, NewLogin, NewFullName, pass, NewRole.Value), "Foydalanuvchi yaratildi"))
        {
            NewLogin = ""; NewFullName = "";
            Load();
        }
    }

    [RelayCommand]
    private void ToggleBlock()
    {
        if (Selected is not { } u) return;
        var block = !u.User.IsBlocked;
        if (block && !Ui.Confirm($"{u.FullName} bloklansinmi? U tizimga kira olmaydi, ma'lumotlari saqlanadi.")) return;
        if (Ui.Run(() => S.Auth.SetBlocked(Session, u.User.Id, block), block ? "Bloklandi" : "Blokdan chiqarildi")) Load();
    }

    [RelayCommand]
    private void ResetPassword()
    {
        if (Selected is not { } u) return;
        var pass = Ui.Ask("Parolni tiklash", $"{u.FullName} uchun yangi parol:");
        if (pass is null) return;
        if (Ui.Run(() => S.Auth.ResetPassword(Session, u.User.Id, pass), "Parol o'zgartirildi")) Load();
    }

    [RelayCommand]
    private void SaveGroups()
    {
        if (Selected is not { } u) return;
        Ui.Run(() => S.Auth.SetObserverGroups(Session, u.User.Id, GroupChecks.Where(g => g.IsChecked).Select(g => g.Group.Id)), "Ruxsat berilgan guruhlar saqlandi");
    }
}
