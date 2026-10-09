using System.Diagnostics;
using System.IO;
using System.Windows;
using AiUstozPro.Application.Security;
using AiUstozPro.Infrastructure;
using Microsoft.Win32;

namespace AiUstozPro.App.Services;

/// <summary>Butun ilova uchun umumiy holat: servislar va joriy sessiya.</summary>
public static class AppState
{
    private static AppServices? _services;
    public static AppServices Services => _services ?? throw new InvalidOperationException("Servislar ishga tushirilmagan.");
    public static UserSession? Session { get; set; }
    public static UserSession RequireSession => Session ?? throw new AccessDeniedException("Tizimga kirilmagan.");
    public static void Init(AppServices services) => _services = services;
}

public static class Log
{
    private static readonly object Gate = new();
    public static string? FilePath { get; set; }

    public static void Write(string level, string message)
    {
        try
        {
            if (FilePath is null) return;
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Jurnalga yozib bo'lmasa — dastur ishini to'xtatmaymiz.
        }
    }

    public static void Error(Exception ex, string context) => Write("ERROR", $"{context}: {ex}");
    public static void Info(string message) => Write("INFO", message);
}

public static class Ui
{
    public const string AppTitle = "AI Ustoz Pro";

    public static Window? Owner =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;

    public static void Info(string message) => Show(message, MessageBoxImage.Information);
    public static void Warn(string message) => Show(message, MessageBoxImage.Warning);
    public static void Error(string message) => Show(message, MessageBoxImage.Error);

    private static void Show(string message, MessageBoxImage icon)
    {
        var owner = Owner;
        if (owner is not null) MessageBox.Show(owner, message, AppTitle, MessageBoxButton.OK, icon);
        else MessageBox.Show(message, AppTitle, MessageBoxButton.OK, icon);
    }

    public static bool Confirm(string message, bool dangerous = false)
    {
        var owner = Owner;
        var icon = dangerous ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var res = owner is not null
            ? MessageBox.Show(owner, message, AppTitle, MessageBoxButton.YesNo, icon, MessageBoxResult.No)
            : MessageBox.Show(message, AppTitle, MessageBoxButton.YesNo, icon, MessageBoxResult.No);
        return res == MessageBoxResult.Yes;
    }

    /// <summary>Amalni bajaradi va xatolarni foydalanuvchiga tushunarli tarzda ko'rsatadi.</summary>
    public static bool Run(Action action, string? successMessage = null)
    {
        try
        {
            action();
            if (successMessage is not null) Toast.Show(successMessage);
            return true;
        }
        catch (BusinessRuleException ex) { Warn(ex.Message); }
        catch (AccessDeniedException ex) { Warn(ex.Message); }
        catch (Exception ex)
        {
            Log.Error(ex, "Amal bajarilmadi");
            Error($"Kutilmagan xato yuz berdi. Ma'lumotlar o'zgartirilmadi.\n\n{ex.Message}\n\nBatafsil: {Log.FilePath}");
        }
        return false;
    }

    public static string? Ask(string title, string prompt, string initial = "", bool required = true)
    {
        var dlg = new Dialogs.InputDialog(title, prompt, initial, required) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public static string? OpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        var o = Owner;
        return (o is not null ? dlg.ShowDialog(o) : dlg.ShowDialog()) == true ? dlg.FileName : null;
    }

    public static string? SaveFile(string defaultName, string filter)
    {
        var dlg = new SaveFileDialog { FileName = defaultName, Filter = filter, AddExtension = true, OverwritePrompt = true };
        var o = Owner;
        return (o is not null ? dlg.ShowDialog(o) : dlg.ShowDialog()) == true ? dlg.FileName : null;
    }

    public static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Warn($"Faylni ochib bo'lmadi: {ex.Message}"); }
    }

    public static void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { Warn($"Papkani ochib bo'lmadi: {ex.Message}"); }
    }

    public static DateOnly? ToDateOnly(DateTime? d) => d is { } x ? DateOnly.FromDateTime(x) : null;
    public static DateTime? ToDateTime(DateOnly? d) => d is { } x ? x.ToDateTime(TimeOnly.MinValue) : null;

    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        var s = (text ?? "").Trim().Replace('.', ':');
        return TimeOnly.TryParseExact(s, new[] { "H:mm", "HH:mm" }, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out time);
    }
}

/// <summary>Asosiy oynaning pastki qismida qisqa bildirishnoma.</summary>
public static class Toast
{
    public static event Action<string, bool>? Requested;
    public static void Show(string message, bool isError = false) => Requested?.Invoke(message, isError);
}
