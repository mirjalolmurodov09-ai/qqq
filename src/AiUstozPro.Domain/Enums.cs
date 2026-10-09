namespace AiUstozPro.Domain;

public enum UserRole
{
    Administrator = 1,
    Teacher = 2,
    Observer = 3,
}

public enum AttendanceStatus
{
    Present = 1,
    Late = 2,
    AbsentExcused = 3,
    AbsentUnexcused = 4,
    LeftWithPermission = 5,
    Unidentified = 6,
}

/// <summary>Kalendar istisnosi turi.</summary>
public enum CalendarExceptionKind
{
    /// <summary>Rasmiy bayram — dars o'tilmaydi.</summary>
    Holiday = 1,
    /// <summary>Qo'shimcha dam olish kuni — dars o'tilmaydi.</summary>
    ExtraDayOff = 2,
    /// <summary>Ta'til davri (StartDate..EndDate) — dars o'tilmaydi.</summary>
    Vacation = 3,
    /// <summary>Ko'chirilgan ish kuni: shu sana boshqa hafta kuni jadvali bo'yicha ishlaydi.</summary>
    TransferredWorkday = 4,
    /// <summary>Muayyan guruh/fan uchun dars o'tilmaydigan kun.</summary>
    NoLessons = 5,
}

public enum LessonStatus
{
    Planned = 1,
    Conducted = 2,
    Cancelled = 3,
    /// <summary>Bayram/ta'til sababli o'tilmaydigan sana (ma'lumot uchun).</summary>
    Skipped = 4,
}

public enum LessonOrigin
{
    /// <summary>Jadval va kalendardan avtomatik hisoblangan.</summary>
    Generated = 1,
    /// <summary>Foydalanuvchi qo'lda qo'shgan (qo'shimcha dars).</summary>
    Manual = 2,
    /// <summary>Avtomatik hisoblangan, keyin foydalanuvchi boshqa sanaga ko'chirgan.</summary>
    Moved = 3,
}

public enum TopicType
{
    Theory = 1,
    Practice = 2,
    Laboratory = 3,
    Control = 4,
}

public enum TopicStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    NotPlaced = 4,
}

public enum TopicPlacementMode
{
    /// <summary>Mavzu soatlari ketma-ket darslarga bo'linadi (masalan 4 soat = 2 ta 2 soatlik dars).</summary>
    SplitByHours = 1,
    /// <summary>Har bir mavzu bitta darsga joylashtiriladi, soatidan qat'i nazar.</summary>
    OneLessonPerTopic = 2,
}

public static class EnumText
{
    public static string ToUz(this UserRole r) => r switch
    {
        UserRole.Administrator => "Administrator",
        UserRole.Teacher => "O'qituvchi",
        UserRole.Observer => "Kuzatuvchi (rahbar)",
        _ => r.ToString(),
    };

    public static string ToUz(this AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => "Keldi",
        AttendanceStatus.Late => "Kechikdi",
        AttendanceStatus.AbsentExcused => "Sababli kelmadi",
        AttendanceStatus.AbsentUnexcused => "Sababsiz kelmadi",
        AttendanceStatus.LeftWithPermission => "Ruxsat bilan chiqdi",
        AttendanceStatus.Unidentified => "Aniqlanmadi — tekshirish kerak",
        _ => s.ToString(),
    };

    public static string ToShortUz(this AttendanceStatus s) => s switch
    {
        AttendanceStatus.Present => "+",
        AttendanceStatus.Late => "K",
        AttendanceStatus.AbsentExcused => "S",
        AttendanceStatus.AbsentUnexcused => "Y",
        AttendanceStatus.LeftWithPermission => "R",
        AttendanceStatus.Unidentified => "?",
        _ => "",
    };

    public static string ToUz(this CalendarExceptionKind k) => k switch
    {
        CalendarExceptionKind.Holiday => "Bayram",
        CalendarExceptionKind.ExtraDayOff => "Qo'shimcha dam olish",
        CalendarExceptionKind.Vacation => "Ta'til",
        CalendarExceptionKind.TransferredWorkday => "Ko'chirilgan ish kuni",
        CalendarExceptionKind.NoLessons => "Dars o'tilmaydi",
        _ => k.ToString(),
    };

    public static string ToUz(this LessonStatus s) => s switch
    {
        LessonStatus.Planned => "Rejalashtirilgan",
        LessonStatus.Conducted => "O'tildi",
        LessonStatus.Cancelled => "Bekor qilindi",
        LessonStatus.Skipped => "Dars yo'q",
        _ => s.ToString(),
    };

    public static string ToUz(this LessonOrigin o) => o switch
    {
        LessonOrigin.Generated => "Avtomatik",
        LessonOrigin.Manual => "Qo'shimcha",
        LessonOrigin.Moved => "Ko'chirilgan",
        _ => o.ToString(),
    };

    public static string ToUz(this TopicType t) => t switch
    {
        TopicType.Theory => "Nazariy",
        TopicType.Practice => "Amaliy",
        TopicType.Laboratory => "Laboratoriya",
        TopicType.Control => "Nazorat",
        _ => t.ToString(),
    };

    public static string ToUz(this TopicStatus s) => s switch
    {
        TopicStatus.Planned => "Rejalashtirilgan",
        TopicStatus.InProgress => "Davom etmoqda",
        TopicStatus.Completed => "O'tildi",
        TopicStatus.NotPlaced => "Sana yetmadi",
        _ => s.ToString(),
    };

    public static string ToUz(this DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Dushanba",
        DayOfWeek.Tuesday => "Seshanba",
        DayOfWeek.Wednesday => "Chorshanba",
        DayOfWeek.Thursday => "Payshanba",
        DayOfWeek.Friday => "Juma",
        DayOfWeek.Saturday => "Shanba",
        DayOfWeek.Sunday => "Yakshanba",
        _ => d.ToString(),
    };
}
