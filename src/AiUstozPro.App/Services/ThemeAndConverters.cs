using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AiUstozPro.Domain;

namespace AiUstozPro.App.Services;

public static class ThemeService
{
    public const string SettingKey = "Theme";
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        var app = System.Windows.Application.Current;
        var dicts = app.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d => d.Source is not null && d.Source.OriginalString.Contains("Colors."));
        var uri = new Uri(dark ? "pack://application:,,,/AiUstozPro;component/Themes/Colors.Dark.xaml" : "pack://application:,,,/AiUstozPro;component/Themes/Colors.Light.xaml", UriKind.Absolute);
        var fresh = new ResourceDictionary { Source = uri };
        if (old is not null) dicts[dicts.IndexOf(old)] = fresh;
        else dicts.Insert(0, fresh);
        IsDark = dark;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool x && x;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null && !(value is string s && s.Length == 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool ZeroVisible { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var n = value is int i ? i : 0;
        return (n == 0) == ZeroVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Enum qiymatini o'zbekcha matnga aylantiradi.</summary>
public sealed class EnumUzConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AttendanceStatus a => a.ToUz(),
        LessonStatus l => l.ToUz(),
        LessonOrigin o => o.ToUz(),
        TopicType t => t.ToUz(),
        TopicStatus s => s.ToUz(),
        CalendarExceptionKind k => k.ToUz(),
        UserRole r => r.ToUz(),
        DayOfWeek d => d.ToUz(),
        AssessmentStatus st => st.ToUz(),
        ResultMethod rm => rm.ToUz(),
        null => "",
        _ => value.ToString() ?? "",
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DateOnlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateOnly d => d.ToString("dd.MM.yyyy"),
        DateTime dt => dt.ToString("dd.MM.yyyy"),
        _ => "",
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Ro'yxatlarda tanlash uchun "qiymat — o'zbekcha nom" juftligi.</summary>
public sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

public static class Choices
{
    public static List<Choice<AttendanceStatus>> AttendanceStatuses { get; } =
        Enum.GetValues<AttendanceStatus>().Select(s => new Choice<AttendanceStatus>(s, s.ToUz())).ToList();

    public static List<Choice<CalendarExceptionKind>> ExceptionKinds { get; } =
        Enum.GetValues<CalendarExceptionKind>().Select(s => new Choice<CalendarExceptionKind>(s, s.ToUz())).ToList();

    public static List<Choice<TopicType>> TopicTypes { get; } =
        Enum.GetValues<TopicType>().Select(s => new Choice<TopicType>(s, s.ToUz())).ToList();

    public static List<Choice<UserRole>> Roles { get; } =
        Enum.GetValues<UserRole>().Select(s => new Choice<UserRole>(s, s.ToUz())).ToList();

    public static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public static List<Choice<DayOfWeek>> Days { get; } = WeekOrder.Select(d => new Choice<DayOfWeek>(d, d.ToUz())).ToList();
}
