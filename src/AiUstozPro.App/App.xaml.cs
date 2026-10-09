using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.App.ViewModels;
using AiUstozPro.Infrastructure;

namespace AiUstozPro.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? dataDir = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data-dir") dataDir = args[i + 1];

        if (args.Contains("--smoke-test"))
        {
            dataDir ??= Path.Combine(Path.GetTempPath(), "AiUstozPro-smoke");
            Shutdown(RunSmoke(dataDir));
            return;
        }

        // O'zbekcha sana formati (dd.MM.yyyy) va hafta dushanbadan boshlanishi.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "dd.MM.yyyy";
        culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("uz-Latn-UZ")));

        dataDir ??= AppPaths.DefaultDataDirectory;
        Log.FilePath = Path.Combine(dataDir, "logs", $"aiustoz-{DateTime.Now:yyyyMM}.log");
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log.Write("FATAL", ev.ExceptionObject?.ToString() ?? "");

        try
        {
            AppState.Init(AppServices.Open(dataDir));
            Log.Info($"Dastur ishga tushdi. Ma'lumotlar: {dataDir}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ma'lumotlar bazasini ochib bo'lmadi");
            MessageBox.Show($"Ma'lumotlar bazasini ochib bo'lmadi:\n\n{ex.Message}\n\nPapka: {dataDir}", Ui.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        try
        {
            var theme = AppState.Services.Settings.Get(ThemeService.SettingKey);
            if (theme == "dark") ThemeService.Apply(true);
        }
        catch (Exception ex) { Log.Error(ex, "Mavzuni o'qib bo'lmadi"); }

        try { AppState.Services.Backup.AutoBackupIfDue(); }
        catch (Exception ex) { Log.Error(ex, "Avtomatik zaxira nusxa yaratilmadi"); }

        ShowLogin();
    }

    private void ShowLogin()
    {
        var login = new LoginWindow();
        if (login.ShowDialog() != true || login.Session is null)
        {
            Shutdown(0);
            return;
        }
        AppState.Session = login.Session;
        var vm = new MainViewModel();
        var main = new MainWindow(vm);
        MainWindow = main;
        vm.LogoutRequested += () =>
        {
            AppState.Session = null;
            main.Close();
        };
        main.Closed += (_, _) =>
        {
            if (AppState.Session is null) ShowLogin();
            else Shutdown(0);
        };
        main.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Ushlanmagan xato");
        MessageBox.Show($"Kutilmagan xato yuz berdi:\n\n{e.Exception.Message}\n\nDastur ishlashda davom etadi. Batafsil jurnal: {Log.FilePath}",
            Ui.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static int RunSmoke(string dataDir)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(dataDir))!;
        Directory.CreateDirectory(parent);
        var logPath = Path.Combine(parent, Path.GetFileName(dataDir) + "-smoke.log");
        var lines = new List<string>();
        int code;
        try
        {
            SmokeScenario.Run(dataDir, lines.Add);
            // WPF resurslari va oynalar haqiqatan yuklanishini tekshirish (ko'rsatmasdan).
            var window = new MainWindowProbe();
            lines.Add(window.Probe());
            code = 0;
        }
        catch (Exception ex)
        {
            lines.Add("XATO: " + ex);
            code = 1;
        }
        Directory.CreateDirectory(dataDir);
        File.WriteAllLines(Path.Combine(dataDir, "smoke-test.log"), lines);
        File.WriteAllLines(logPath, lines);
        return code;
    }
}
