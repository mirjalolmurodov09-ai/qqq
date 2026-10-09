using AiUstozPro.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<ObserverGroupAccess> ObserverGroupAccess => Set<ObserverGroupAccess>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<AcademicCalendar> AcademicCalendars => Set<AcademicCalendar>();
    public DbSet<AcademicTerm> AcademicTerms => Set<AcademicTerm>();
    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<LessonOccurrence> LessonOccurrences => Set<LessonOccurrence>();
    public DbSet<CurriculumPlan> CurriculumPlans => Set<CurriculumPlan>();
    public DbSet<CurriculumTopic> CurriculumTopics => Set<CurriculumTopic>();
    public DbSet<TopicAssignment> TopicAssignments => Set<TopicAssignment>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<BackupHistory> BackupHistory => Set<BackupHistory>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).HasMaxLength(64).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<int>();
        });

        b.Entity<ObserverGroupAccess>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.GroupId }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.GroupAccess).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Group>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<Student>(e =>
        {
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.MiddleName).HasMaxLength(100);
            e.Property(x => x.StudentNumber).HasMaxLength(50);
            e.HasIndex(x => x.StudentNumber).IsUnique().HasFilter("\"StudentNumber\" IS NOT NULL");
            e.HasIndex(x => new { x.GroupId, x.LastName, x.FirstName });
            e.HasOne(x => x.Group).WithMany(g => g.Students).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.FullName);
        });

        b.Entity<Subject>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        b.Entity<AcademicCalendar>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasMany(x => x.Terms).WithOne().HasForeignKey(t => t.AcademicCalendarId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Exceptions).WithOne().HasForeignKey(t => t.AcademicCalendarId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CalendarException>(e =>
        {
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.WorksAsDayOfWeek).HasConversion<int?>();
            e.HasIndex(x => new { x.AcademicCalendarId, x.StartDate });
        });

        b.Entity<TimetableEntry>(e =>
        {
            e.Property(x => x.DayOfWeek).HasConversion<int>();
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicCalendar>().WithMany().HasForeignKey(x => x.AcademicCalendarId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademicCalendarId, x.DayOfWeek, x.LessonNumber });
        });

        b.Entity<LessonOccurrence>(e =>
        {
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Origin).HasConversion<int>();
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TimetableEntry).WithMany().HasForeignKey(x => x.TimetableEntryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GroupId, x.SubjectId, x.Date });
            e.HasIndex(x => new { x.TimetableEntryId, x.Date });
            e.HasIndex(x => x.Date);
        });

        b.Entity<CurriculumPlan>(e =>
        {
            e.Property(x => x.PlacementMode).HasConversion<int>();
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicCalendar>().WithMany().HasForeignKey(x => x.AcademicCalendarId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Topics).WithOne(t => t.Plan).HasForeignKey(t => t.CurriculumPlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.GroupId, x.SubjectId, x.AcademicCalendarId }).IsUnique();
        });

        b.Entity<CurriculumTopic>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => new { x.CurriculumPlanId, x.OrderNo });
        });

        b.Entity<TopicAssignment>(e =>
        {
            e.HasOne(x => x.Topic).WithMany(t => t.Assignments).HasForeignKey(x => x.CurriculumTopicId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonOccurrenceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CurriculumTopicId, x.LessonOccurrenceId }).IsUnique();
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonOccurrenceId).OnDelete(DeleteBehavior.Restrict);
            // Bir o'quvchi uchun bitta darsda faqat bitta davomat yozuvi.
            e.HasIndex(x => new { x.StudentId, x.LessonOccurrenceId }).IsUnique();
            e.HasIndex(x => x.LessonOccurrenceId);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.TimestampUtc);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
        });

        b.Entity<AppSetting>(e => e.HasIndex(x => x.Key).IsUnique());
    }
}

public interface IDbFactory
{
    AppDbContext Create();
    string DatabasePath { get; }
}

public sealed class SqliteDbFactory : IDbFactory
{
    public SqliteDbFactory(string databasePath)
    {
        DatabasePath = databasePath;
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };
        ConnectionString = csb.ToString();
    }

    public string DatabasePath { get; }
    public string ConnectionString { get; }

    public AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
