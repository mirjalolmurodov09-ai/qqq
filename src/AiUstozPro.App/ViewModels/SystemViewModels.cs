using System.Collections.ObjectModel;
using System.Diagnostics;
using AiUstozPro.App.Services;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed partial class BackupViewModel : PageViewModel
{
    public override string Title => "Zaxira nusxalar";
    public ObservableCollection<BackupHistory> History { get; } = new();
    [ObservableProperty] private BackupHistory? _selected;
    public string Folder => S.Backup.BackupDirectory;

    public override void OnActivated() => Load();

    private void Load()
    {
        Ui.Run(() =>
        {
            History.Clear();
            foreach (var h in S.Backup.ListHistory()) History.Add(h);
        });
    }

    [RelayCommand]
    private void Create()
    {
        BackupHistory? h = null;
        if (Ui.Run(() => h = S.Backup.CreateBackup(Session)))
        {
            Toast.Show($"Zaxira nusxa yaratildi ({h!.SizeBytes / 1024} KB)");
            Load();
        }
    }

    [RelayCommand]
    private void SaveCopy()
    {
        var path = Ui.SaveFile($"AiUstozPro-zaxira-{DateTime.Now:yyyyMMdd-HHmm}.db", "AI Ustoz Pro zaxira (*.db)|*.db");
        if (path is null) return;
        if (Ui.Run(() => S.Backup.CreateBackup(Session, "manual", path), "Nusxa saqlandi")) { Load(); Ui.ShowInFolder(path); }
    }

    [RelayCommand]
    private void OpenFolder() => Ui.OpenPath(Folder);

    [RelayCommand]
    private void Restore()
    {
        var path = Selected?.FilePath;
        if (path is null || !System.IO.File.Exists(path))
            path = Ui.OpenFile("AI Ustoz Pro zaxira (*.db)|*.db");
        if (path is null) return;
        if (!BackupService.IsValidBackup(path, out var err)) { Ui.Warn($"Bu fayldan tiklab bo'lmaydi: {err}"); return; }
        if (!Ui.Confirm($"Joriy ma'lumotlar quyidagi zaxira nusxa bilan ALMASHTIRILADI:\n{path}\n\nAvval joriy holat avtomatik saqlanadi. Tiklangandan so'ng dastur qayta ishga tushadi. Davom etasizmi?", dangerous: true))
            return;
        string safety = "";
        if (Ui.Run(() => safety = S.Backup.Restore(Session, path)))
        {
            Ui.Info($"Ma'lumotlar tiklandi. Oldingi holat nusxasi: {safety}\n\nDastur qayta ishga tushiriladi.");
            try
            {
                var exe = Environment.ProcessPath;
                if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error(ex, "Qayta ishga tushirish"); }
            System.Windows.Application.Current.Shutdown(0);
        }
    }
}

public sealed partial class SettingsViewModel : PageViewModel
{
    public override string Title => "Sozlamalar";
    public string DataFolder => S.DataDirectory;
    public string DatabasePath => S.Factory.DatabasePath;
    public string LogFolder => S.LogDirectory;
    public string SchemaVersion => $"Ma'lumotlar bazasi sxemasi: v{DatabaseMigrator.CurrentVersion}";
    public string UserInfo => $"{Session.FullName} ({Session.Login}), {Session.Role.ToUz()}";

    [ObservableProperty] private bool _isDark;

    public override void OnActivated() => IsDark = ThemeService.IsDark;

    partial void OnIsDarkChanged(bool value)
    {
        if (value == ThemeService.IsDark) return;
        ThemeService.Apply(value);
        Ui.Run(() => S.Settings.Set(ThemeService.SettingKey, value ? "dark" : "light"));
    }

    [RelayCommand]
    private void ChangePassword()
    {
        var p1 = Ui.Ask("Parolni o'zgartirish", "Yangi parol (kamida 8 belgi, harf va raqam):");
        if (p1 is null) return;
        var p2 = Ui.Ask("Parolni o'zgartirish", "Yangi parolni takrorlang:");
        if (p2 is null) return;
        if (p1 != p2) { Ui.Warn("Parollar mos kelmadi."); return; }
        Ui.Run(() => S.Auth.ResetPassword(Session, Session.UserId, p1), "Parol o'zgartirildi");
    }

    [RelayCommand] private void OpenDataFolder() => Ui.OpenPath(DataFolder);
    [RelayCommand] private void OpenLogFolder() => Ui.OpenPath(LogFolder);
}

public sealed class ModuleStatus
{
    public required string Name { get; init; }
    public required string State { get; init; }
}

public sealed class AboutViewModel : PageViewModel
{
    public override string Title => "Dastur haqida";
    public string Version => "Versiya 0.1.0";
    public string Author => "Muallif: Murodov M., Informatika o'qituvchisi";

    public List<ModuleStatus> Modules { get; } = new()
    {
        new() { Name = "Kirish, rollar (administrator, o'qituvchi, kuzatuvchi), audit jurnali", State = "Tayyor" },
        new() { Name = "Guruhlar, o'quvchilar, fanlar; Excel/CSV import va eksport", State = "Tayyor" },
        new() { Name = "O'quv kalendari: ish kunlari, bayramlar, ta'til, ko'chirilgan ish kunlari", State = "Tayyor" },
        new() { Name = "Dars jadvali: haftalik ko'rinish, ziddiyatlarni aniqlash, import/eksport", State = "Tayyor" },
        new() { Name = "Dars sanalarini avtomatik hisoblash, ko'rib chiqish, qo'lda tahrirlash", State = "Tayyor" },
        new() { Name = "KTR: import, sanalarga joylashtirish, reja farqi, o'tildi belgisi", State = "Tayyor" },
        new() { Name = "Qo'lda davomat va tuzatishlar tarixi", State = "Tayyor" },
        new() { Name = "Hisobotlar: Excel, PDF, CSV", State = "Tayyor" },
        new() { Name = "Zaxira nusxa va tiklash, avtomatik kunlik zaxira", State = "Tayyor" },
        new() { Name = "Hisobotlarni Word (.docx) formatida", State = "Keyingi versiya" },
        new() { Name = "AI yordamchi (Claude / OpenAI / lokal Ollama)", State = "Keyingi versiya (v0.2)" },
        new() { Name = "Yuz orqali davomat (veb-kamera, rozilik bilan)", State = "Keyingi versiya" },
        new() { Name = "Ovozli yordamchi", State = "Keyingi versiya" },
        new() { Name = "Test va baholash, darsni boshqarish vositalari", State = "Keyingi versiya" },
        new() { Name = "Rus va ingliz tillari, PostgreSQL", State = "Rejada" },
    };
}
