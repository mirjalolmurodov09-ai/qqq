using System.Collections.ObjectModel;
using System.Diagnostics;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Ai;
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
    private void SaveEncrypted()
    {
        var path = Ui.SaveFile($"AiUstozPro-zaxira-{DateTime.Now:yyyyMMdd-HHmm}{BackupCrypto.Extension}", $"Shifrlangan zaxira (*{BackupCrypto.Extension})|*{BackupCrypto.Extension}");
        if (path is null) return;
        var pass = Ui.AskPassword("Zaxira paroli", "Nusxa shu parol bilan shifrlanadi (AES-256). Parolni unutsangiz, nusxani tiklab BO'LMAYDI — uni xavfsiz joyga yozib qo'ying.", confirm: true);
        if (pass is null) return;
        if (Ui.Run(() => S.Backup.ExportEncrypted(Session, path, pass), "Shifrlangan nusxa saqlandi")) { Load(); Ui.ShowInFolder(path); }
    }

    [RelayCommand]
    private void OpenFolder() => Ui.OpenPath(Folder);

    [RelayCommand]
    private void Restore()
    {
        var path = Selected?.FilePath;
        if (path is null || !System.IO.File.Exists(path))
            path = Ui.OpenFile($"AI Ustoz Pro zaxira (*.db;*{BackupCrypto.Extension})|*.db;*{BackupCrypto.Extension}");
        if (path is null) return;
        string? password = null;
        if (BackupCrypto.IsEncrypted(path))
        {
            password = Ui.AskPassword("Shifrlangan nusxa", "Bu nusxa shifrlangan. Parolni kiriting:", confirm: false);
            if (password is null) return;
        }
        else if (!BackupService.IsValidBackup(path, out var err)) { Ui.Warn($"Bu fayldan tiklab bo'lmaydi: {err}"); return; }
        if (!Ui.Confirm($"Joriy ma'lumotlar quyidagi zaxira nusxa bilan ALMASHTIRILADI:\n{path}\n\nAvval joriy holat avtomatik saqlanadi. Tiklangandan so'ng dastur qayta ishga tushadi. Davom etasizmi?", dangerous: true))
            return;
        string safety = "";
        if (Ui.Run(() => safety = S.Backup.Restore(Session, path, password)))
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
    public bool CanManageAi => Can(AiUstozPro.Application.Security.Permission.ManageSettings);

    public List<Choice<AiProviderKind>> Providers { get; } = Enum.GetValues<AiProviderKind>()
        .Select(p => new Choice<AiProviderKind>(p, AiSettings.ProviderName(p))).ToList();

    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private Choice<AiProviderKind>? _aiProvider;
    [ObservableProperty] private string _aiModel = "";
    [ObservableProperty] private string _aiBaseUrl = "";
    [ObservableProperty] private string _aiMaxTokens = "2000";
    [ObservableProperty] private string _aiHourlyLimit = "30";
    [ObservableProperty] private string _aiTimeout = "90";
    [ObservableProperty] private string _aiKeyState = "";
    [ObservableProperty] private string _aiTestResult = "";
    [ObservableProperty] private bool _aiNeedsKey;
    [ObservableProperty] private bool _aiBusy;

    private bool _loading;

    public override void OnActivated()
    {
        IsDark = ThemeService.IsDark;
        Ui.Run(() =>
        {
            _loading = true;
            var s = S.Ai.GetSettings();
            AiProvider = Providers.First(p => p.Value == s.Provider);
            AiModel = s.Model;
            AiBaseUrl = s.BaseUrl ?? "";
            AiMaxTokens = s.MaxTokens.ToString();
            AiHourlyLimit = s.HourlyLimit.ToString();
            AiTimeout = s.TimeoutSeconds.ToString();
            _loading = false;
            UpdateKeyState();
        });
    }

    partial void OnAiProviderChanged(Choice<AiProviderKind>? value)
    {
        AiNeedsKey = value?.Value is AiProviderKind.Anthropic or AiProviderKind.OpenAI;
        if (!_loading && value is not null) AiModel = AiSettings.DefaultModel(value.Value);
        UpdateKeyState();
    }

    private void UpdateKeyState()
    {
        if (AiProvider is null) return;
        AiKeyState = !AiNeedsKey ? "Bu provayder uchun API kaliti kerak emas."
            : S.Ai.HasApiKey(AiProvider.Value) ? "API kaliti saqlangan (Windows hisob ma'lumotlari menejerida)." : "API kaliti kiritilmagan.";
    }

    private AiSettings? BuildAi()
    {
        if (AiProvider is null) return null;
        if (!int.TryParse(AiMaxTokens, out var mt) || !int.TryParse(AiHourlyLimit, out var hl) || !int.TryParse(AiTimeout, out var to))
        {
            Ui.Warn("Token, limit va kutish vaqti butun son bo'lishi kerak.");
            return null;
        }
        return new AiSettings { Provider = AiProvider.Value, Model = AiModel, BaseUrl = AiBaseUrl, MaxTokens = mt, HourlyLimit = hl, TimeoutSeconds = to };
    }

    [RelayCommand]
    private void SaveAi(object? passwordBox)
    {
        var s = BuildAi();
        if (s is null) return;
        var key = (passwordBox as System.Windows.Controls.PasswordBox)?.Password;
        if (Ui.Run(() =>
        {
            S.Ai.SaveSettings(Session, s);
            if (!string.IsNullOrWhiteSpace(key)) S.Ai.SetApiKey(Session, s.Provider, key);
        }, "AI sozlamalari saqlandi"))
        {
            (passwordBox as System.Windows.Controls.PasswordBox)?.Clear();
            UpdateKeyState();
        }
    }

    [RelayCommand]
    private void DeleteAiKey()
    {
        if (AiProvider is null || !AiNeedsKey) return;
        if (!Ui.Confirm("Saqlangan API kaliti o'chirilsinmi?")) return;
        if (Ui.Run(() => S.Ai.SetApiKey(Session, AiProvider.Value, null), "API kaliti o'chirildi")) UpdateKeyState();
    }

    [RelayCommand]
    private async Task TestAiAsync(object? passwordBox)
    {
        var s = BuildAi();
        if (s is null) return;
        var key = (passwordBox as System.Windows.Controls.PasswordBox)?.Password;
        AiBusy = true;
        AiTestResult = "Tekshirilmoqda…";
        try
        {
            AiTestResult = await S.Ai.TestConnectionAsync(Session, s, key, CancellationToken.None);
        }
        catch (AiException ex) { AiTestResult = "Xato: " + ex.Message; }
        catch (Exception ex)
        {
            Log.Error(ex, "AI ulanish tekshiruvi");
            AiTestResult = "Xato: " + ex.Message;
        }
        finally { AiBusy = false; }
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (value == ThemeService.IsDark) return;
        ThemeService.Apply(value);
        Ui.Run(() => S.Settings.Set(ThemeService.SettingKey, value ? "dark" : "light"));
    }

    [RelayCommand]
    private void ChangePassword()
    {
        var p1 = Ui.AskPassword("Parolni o'zgartirish", "Yangi parol (kamida 8 belgi, harf va raqam):", confirm: true);
        if (p1 is null) return;
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
    public string Version => "Versiya 0.2.0";
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
        new() { Name = "Hisobotlar: Excel, Word (.docx), PDF, CSV", State = "Tayyor" },
        new() { Name = "Zaxira nusxa va tiklash, avtomatik kunlik zaxira, parol bilan shifrlangan nusxa (AES-256)", State = "Tayyor" },
        new() { Name = "AI yordamchi: Claude / OpenAI / lokal Ollama, 13 ta topshiriq shabloni, o'qituvchi tasdig'i", State = "Tayyor (v0.2)" },
        new() { Name = "Yuz orqali davomat (veb-kamera, rozilik bilan)", State = "Keyingi versiya" },
        new() { Name = "Ovozli yordamchi", State = "Keyingi versiya" },
        new() { Name = "Test va baholash, darsni boshqarish vositalari", State = "Keyingi versiya" },
        new() { Name = "Rus va ingliz tillari, PostgreSQL", State = "Rejada" },
    };
}
