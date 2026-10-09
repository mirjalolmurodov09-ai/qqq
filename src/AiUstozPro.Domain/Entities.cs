namespace AiUstozPro.Domain;

public abstract class Entity
{
    public int Id { get; set; }
}

public class User : Entity
{
    public string Login { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsBlocked { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>Kuzatuvchi uchun ruxsat berilgan guruhlar.</summary>
    public List<ObserverGroupAccess> GroupAccess { get; set; } = new();
}

public class ObserverGroupAccess : Entity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
}

public class Group : Entity
{
    public string Name { get; set; } = "";
    public string? Course { get; set; }
    public string? Note { get; set; }
    public bool IsArchived { get; set; }
    public List<Student> Students { get; set; } = new();
}

public class Student : Entity
{
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    /// <summary>O'quvchi raqami — tizim bo'yicha noyob (agar kiritilgan bo'lsa).</summary>
    public string? StudentNumber { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string FullName => string.Join(" ", new[] { LastName, FirstName, MiddleName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public class Subject : Entity
{
    public string Name { get; set; } = "";
    public string? Code { get; set; }
}

public class AcademicCalendar : Entity
{
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Ish kunlari bitmaskasi: bit (int)DayOfWeek. Masalan Du–Sha = 0b1111110.</summary>
    public int WorkingDaysMask { get; set; } = 0b0111110;
    public bool IsActive { get; set; } = true;
    public List<AcademicTerm> Terms { get; set; } = new();
    public List<CalendarException> Exceptions { get; set; } = new();

    public bool IsWorkingDay(DayOfWeek d) => (WorkingDaysMask & (1 << (int)d)) != 0;

    public static int MaskOf(params DayOfWeek[] days) => days.Aggregate(0, (m, d) => m | (1 << (int)d));
}

public class AcademicTerm : Entity
{
    public int AcademicCalendarId { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class CalendarException : Entity
{
    public int AcademicCalendarId { get; set; }
    public CalendarExceptionKind Kind { get; set; }
    public DateOnly StartDate { get; set; }
    /// <summary>Faqat ta'til uchun; boshqa turlarda StartDate bilan teng.</summary>
    public DateOnly EndDate { get; set; }
    /// <summary>Ko'chirilgan ish kuni qaysi hafta kuni jadvali bo'yicha ishlashi.</summary>
    public DayOfWeek? WorksAsDayOfWeek { get; set; }
    public string Title { get; set; } = "";
    /// <summary>null bo'lsa barcha guruhlarga tegishli.</summary>
    public int? GroupId { get; set; }
    /// <summary>null bo'lsa barcha fanlarga tegishli.</summary>
    public int? SubjectId { get; set; }
    /// <summary>Administrator rasmiy manba bo'yicha tasdiqlagan.</summary>
    public bool IsConfirmed { get; set; }
    public string? Source { get; set; }

    public bool Covers(DateOnly d) => d >= StartDate && d <= EndDate;
}

public class TimetableEntry : Entity
{
    public int AcademicCalendarId { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int TeacherUserId { get; set; }
    public User? Teacher { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int LessonNumber { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Room { get; set; }
    /// <summary>Bir dars nechta akademik soatga teng (odatda 2 — "para").</summary>
    public int AcademicHours { get; set; } = 2;
    /// <summary>Jadval yozuvi amal qiladigan davr (ixtiyoriy, masalan semestr).</summary>
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public class LessonOccurrence : Entity
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int? TimetableEntryId { get; set; }
    public TimetableEntry? TimetableEntry { get; set; }
    public int TeacherUserId { get; set; }
    public DateOnly Date { get; set; }
    public int LessonNumber { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Room { get; set; }
    public int AcademicHours { get; set; } = 2;
    public LessonStatus Status { get; set; } = LessonStatus.Planned;
    public LessonOrigin Origin { get; set; } = LessonOrigin.Generated;
    /// <summary>Avtomatik hisoblashda dastlabki sana (ko'chirilgan bo'lsa).</summary>
    public DateOnly? OriginalDate { get; set; }
    /// <summary>Foydalanuvchi tasdiqlagan — qayta hisoblashda o'zgartirilmaydi.</summary>
    public bool IsConfirmed { get; set; }
    public string? Note { get; set; }
}

public class CurriculumPlan : Entity
{
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int AcademicCalendarId { get; set; }
    public string Title { get; set; } = "";
    public TopicPlacementMode PlacementMode { get; set; } = TopicPlacementMode.SplitByHours;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<CurriculumTopic> Topics { get; set; } = new();
}

public class CurriculumTopic : Entity
{
    public int CurriculumPlanId { get; set; }
    public CurriculumPlan? Plan { get; set; }
    public int OrderNo { get; set; }
    public string Title { get; set; } = "";
    public int Hours { get; set; } = 2;
    public TopicType Type { get; set; } = TopicType.Theory;
    public string? ExpectedOutcome { get; set; }
    public string? Note { get; set; }
    public TopicStatus Status { get; set; } = TopicStatus.Planned;
    public DateOnly? PlannedDate { get; set; }
    public DateOnly? ActualDate { get; set; }
    public string? TeacherNote { get; set; }
    /// <summary>Qulflangan mavzu qayta joylashtirishda o'z darslarida qoladi.</summary>
    public bool IsLocked { get; set; }
    public List<TopicAssignment> Assignments { get; set; } = new();
}

/// <summary>Mavzu va dars (sana) o'rtasidagi bog'lanish.</summary>
public class TopicAssignment : Entity
{
    public int CurriculumTopicId { get; set; }
    public CurriculumTopic? Topic { get; set; }
    public int LessonOccurrenceId { get; set; }
    public LessonOccurrence? Lesson { get; set; }
    public int Hours { get; set; }
}

public class AttendanceRecord : Entity
{
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int LessonOccurrenceId { get; set; }
    public LessonOccurrence? Lesson { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
    /// <summary>Qanday olingan: "manual", keyinchalik "face".</summary>
    public string Method { get; set; } = "manual";
    public bool IsConfirmed { get; set; } = true;
    public int RecordedByUserId { get; set; }
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedUtc { get; set; }
}

public class AuditLog : Entity
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string UserLogin { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public int? EntityId { get; set; }
    public string? Details { get; set; }
    public string? Reason { get; set; }
}

public class AppSetting : Entity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class BackupHistory : Entity
{
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string FilePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = "manual";
    public string? CreatedBy { get; set; }
}

/// <summary>AI yordamchi bilan suhbat.</summary>
public class AiConversation : Entity
{
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string TaskKey { get; set; } = "chat";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<AiMessage> Messages { get; set; } = new();
}

public class AiMessage : Entity
{
    public int ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    /// <summary>"user" yoki "assistant".</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    /// <summary>O'qituvchi AI javobini tekshirib tasdiqlagan.</summary>
    public bool IsReviewed { get; set; }
    public DateTime? ReviewedUtc { get; set; }
}

/// <summary>Test (nazorat ishi).</summary>
public class Assessment : Entity
{
    public string Title { get; set; } = "";
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int? CurriculumTopicId { get; set; }
    public string? Description { get; set; }
    /// <summary>0 — vaqt cheklanmagan.</summary>
    public int TimeLimitMinutes { get; set; } = 20;
    public bool ShuffleQuestions { get; set; } = true;
    public bool ShuffleOptions { get; set; } = true;
    /// <summary>Chop etiladigan variantlar soni (1–8).</summary>
    public int VariantCount { get; set; } = 2;
    /// <summary>Baholash mezoni: "5" uchun minimal foiz va h.k.</summary>
    public int Grade5Min { get; set; } = 86;
    public int Grade4Min { get; set; } = 71;
    public int Grade3Min { get; set; } = 56;
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Draft;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<TestQuestion> Questions { get; set; } = new();
}

public class TestQuestion : Entity
{
    public int AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }
    public int OrderNo { get; set; }
    public string Text { get; set; } = "";
    public int Points { get; set; } = 1;
    /// <summary>"manual" yoki "ai".</summary>
    public string Source { get; set; } = "manual";
    /// <summary>AI yaratgan savol o'qituvchi tasdiqlamaguncha testga kirmaydi.</summary>
    public bool IsApproved { get; set; } = true;
    public List<TestOption> Options { get; set; } = new();
}

public class TestOption : Entity
{
    public int QuestionId { get; set; }
    public TestQuestion? Question { get; set; }
    public int OrderNo { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

/// <summary>O'quvchining test natijasi (TestResults).</summary>
public class TestResult : Entity
{
    public int AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int GroupId { get; set; }
    public int AttemptNo { get; set; } = 1;
    public DateOnly Date { get; set; }
    public int Variant { get; set; } = 1;
    public ResultMethod Method { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? SubmittedUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public double Percent { get; set; }
    /// <summary>Mezon bo'yicha avtomatik hisoblangan baho.</summary>
    public int AutoGrade { get; set; }
    /// <summary>O'qituvchi tasdiqlagan yakuniy baho.</summary>
    public int? FinalGrade { get; set; }
    public bool IsConfirmed { get; set; }
    public int? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedUtc { get; set; }
    public string? Note { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<TestAnswer> Answers { get; set; } = new();
}

public class TestAnswer : Entity
{
    public int TestResultId { get; set; }
    public TestResult? Result { get; set; }
    public int QuestionId { get; set; }
    public int? SelectedOptionId { get; set; }
    public bool IsCorrect { get; set; }
    public int Points { get; set; }
}
