using System.IO;
using System.Windows;
using System.Windows.Threading;
using AiUstozPro.App.Services;
using AiUstozPro.App.ViewModels;
using AiUstozPro.Infrastructure;

namespace AiUstozPro.App;

/// <summary>
/// Smoke test uchun: asosiy oynani va har bir sahifani haqiqatan yaratib, XAML va bog'lanishlar
/// ishga tushirilganda xato bermasligini tekshiradi (oyna ekrandan tashqarida ochiladi).
/// </summary>
public sealed class MainWindowProbe
{
    public string Probe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aup-ui-probe-" + Guid.NewGuid().ToString("N"));
        var services = AppServices.Open(dir);
        AppState.Init(services);
        var session = services.Auth.CreateInitialAdministrator("probe", "UI Probe", "Probe12345");
        AppState.Session = session;

        var errors = new List<string>();
        void OnError(object s, DispatcherUnhandledExceptionEventArgs e) { errors.Add(e.Exception.ToString()); e.Handled = true; }
        System.Windows.Application.Current.DispatcherUnhandledException += OnError;
        try
        {
            ThemeService.Apply(false);
            var vm = new MainViewModel();
            var w = new MainWindow(vm)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            };
            w.Show();
            int pages = 0;
            foreach (var dark in new[] { false, true })
            {
                ThemeService.Apply(dark);
                foreach (var item in vm.NavItems)
                {
                    vm.SelectedNav = item;
                    w.UpdateLayout();
                    DoEvents();
                    if (vm.CurrentPage is null) errors.Add($"Sahifa ochilmadi: {item.Title}");
                    pages++;
                }
            }
            w.Close();
            if (errors.Count > 0) throw new InvalidOperationException("UI xatolari:\n" + string.Join("\n", errors));
            return $"OK  Interfeys: {pages / 2} ta sahifa yorug' va qorong'i rejimda xatosiz yuklandi";
        }
        finally
        {
            System.Windows.Application.Current.DispatcherUnhandledException -= OnError;
        }
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
