using System.Collections.ObjectModel;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    protected static AppServices S => AppState.Services;
    protected static UserSession Session => AppState.RequireSession;

    public abstract string Title { get; }

    /// <summary>Sahifa ochilganda chaqiriladi (ma'lumotlarni yangilash).</summary>
    public virtual void OnActivated() { }

    public bool Can(Permission p) => AppState.Session?.Can(p) == true;
    public bool IsAdmin => AppState.Session?.Role == UserRole.Administrator;
}

public sealed class NavItem
{
    public NavItem(string title, string glyph, Type pageType, Func<PageViewModel> factory)
    {
        Title = title; Glyph = glyph; PageType = pageType; Factory = factory;
    }
    public string Title { get; }
    public string Glyph { get; }
    public Type PageType { get; }
    public Func<PageViewModel> Factory { get; }
    public PageViewModel? Instance { get; set; }

    public static NavItem Of<T>(string title, string glyph, Func<T> factory) where T : PageViewModel
        => new(title, glyph, typeof(T), () => factory());
}

public sealed partial class MainViewModel : ObservableObject
{
    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _toastIsError;
    [ObservableProperty] private bool _toastVisible;

    public string UserDisplay => AppState.Session is { } s ? $"{s.FullName} · {s.Role.ToUz()}" : "";
    public string TodayText => $"{DateTime.Today:dd.MM.yyyy}, {DateTime.Today.DayOfWeek.ToUz()}";

    public event Action? LogoutRequested;

    private System.Windows.Threading.DispatcherTimer? _toastTimer;

    public MainViewModel()
    {
        var s = AppState.RequireSession;
        NavItems.Add(NavItem.Of("Bosh sahifa", "", () => new DashboardViewModel(this)));
        NavItems.Add(NavItem.Of("Dars rejimi", "\uE7F4", () => new LessonModeViewModel(this)));
        if (s.Can(Permission.UseAi)) NavItems.Add(NavItem.Of("AI yordamchi", "\uE99A", () => new AiAssistantViewModel()));
        NavItems.Add(NavItem.Of("Guruhlar va o'quvchilar", "", () => new GroupsViewModel()));
        NavItems.Add(NavItem.Of("Fanlar", "", () => new SubjectsViewModel()));
        NavItems.Add(NavItem.Of("O'quv kalendari", "", () => new CalendarViewModel()));
        NavItems.Add(NavItem.Of("Dars jadvali", "", () => new TimetableViewModel()));
        NavItems.Add(NavItem.Of("Dars sanalari", "", () => new LessonsViewModel()));
        NavItems.Add(NavItem.Of("Kalendar-tematik reja", "", () => new CurriculumViewModel()));
        NavItems.Add(NavItem.Of("Davomat", "", () => new AttendanceViewModel()));
        NavItems.Add(NavItem.Of("Test va baholash", "\uE9D5", () => new TestsViewModel()));
        NavItems.Add(NavItem.Of("Hisobotlar", "", () => new ReportsViewModel()));
        if (s.Can(Permission.ManageUsers)) NavItems.Add(NavItem.Of("Foydalanuvchilar", "", () => new UsersViewModel()));
        if (s.Can(Permission.ManageBackups)) NavItems.Add(NavItem.Of("Zaxira nusxalar", "", () => new BackupViewModel()));
        NavItems.Add(NavItem.Of("Sozlamalar", "", () => new SettingsViewModel()));
        NavItems.Add(NavItem.Of("Dastur haqida", "", () => new AboutViewModel()));
        Toast.Requested += OnToast;
        SelectedNav = NavItems[0];
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        try
        {
            value.Instance ??= value.Factory();
            CurrentPage = value.Instance;
            value.Instance.OnActivated();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sahifani ochishda xato");
            Ui.Error($"Sahifani ochib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>Boshqa sahifaga o'tish (masalan, bosh sahifadagi tezkor amallar).</summary>
    public T? NavigateTo<T>() where T : PageViewModel
    {
        var item = NavItems.FirstOrDefault(i => i.PageType == typeof(T));
        if (item is null) return null;
        item.Instance ??= item.Factory();
        if (SelectedNav == item) item.Instance.OnActivated();
        else SelectedNav = item;
        return item.Instance as T;
    }

    private void OnToast(string message, bool isError)
    {
        ToastText = message;
        ToastIsError = isError;
        ToastVisible = true;
        _toastTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Stop();
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(object? sender, EventArgs e)
    {
        ToastVisible = false;
        _toastTimer?.Stop();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var dark = !ThemeService.IsDark;
        ThemeService.Apply(dark);
        Ui.Run(() => AppState.Services.Settings.Set(ThemeService.SettingKey, dark ? "dark" : "light"));
    }

    [RelayCommand]
    private void Logout()
    {
        if (!Ui.Confirm("Tizimdan chiqasizmi?")) return;
        Toast.Requested -= OnToast;
        LogoutRequested?.Invoke();
    }
}
