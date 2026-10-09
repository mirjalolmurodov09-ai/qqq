using AiUstozPro.Application.Ai;
using AiUstozPro.Infrastructure.Ai;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using AiUstozPro.Infrastructure.Voice;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure;

public static class AppPaths
{
    /// <summary>Standart ma'lumotlar papkasi: %LOCALAPPDATA%\AiUstozPro (dastur o'chirilganda ham saqlanadi).</summary>
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUstozPro");
}

/// <summary>Kompozitsiya ildizi: barcha servislar bitta joyda yaratiladi.</summary>
public sealed class AppServices
{
    private AppServices(string dataDir, SqliteDbFactory factory, DatabaseMigrator.MigrationReport migration, ISecretStore secrets)
    {
        DataDirectory = dataDir;
        Factory = factory;
        Migration = migration;
        BackupDirectory = Path.Combine(dataDir, "backups");
        ExportDirectory = Path.Combine(dataDir, "exports");
        LogDirectory = Path.Combine(dataDir, "logs");
        Directory.CreateDirectory(ExportDirectory);
        Directory.CreateDirectory(LogDirectory);
        Auth = new AuthService(factory);
        Academic = new AcademicService(factory);
        Calendar = new CalendarService(factory);
        Curriculum = new CurriculumService(factory);
        Attendance = new AttendanceService(factory);
        Reports = new ReportService(factory);
        Backup = new BackupService(factory, BackupDirectory);
        Settings = new SettingsService(factory);
        Dashboard = new DashboardService(factory);
        Secrets = secrets;
        Ai = new AiService(factory, secrets);
        Tests = new AssessmentService(factory);
        Lessons = new LessonService(factory);
        Voice = new VoiceService(factory, secrets, Ai);
    }

    public string DataDirectory { get; }
    public string BackupDirectory { get; }
    public string ExportDirectory { get; }
    public string LogDirectory { get; }
    public SqliteDbFactory Factory { get; }
    public DatabaseMigrator.MigrationReport Migration { get; }

    public AuthService Auth { get; }
    public AcademicService Academic { get; }
    public CalendarService Calendar { get; }
    public CurriculumService Curriculum { get; }
    public AttendanceService Attendance { get; }
    public ReportService Reports { get; }
    public BackupService Backup { get; }
    public SettingsService Settings { get; }
    public DashboardService Dashboard { get; }
    public ISecretStore Secrets { get; }
    public AiService Ai { get; }
    public AssessmentService Tests { get; }
    public LessonService Lessons { get; }
    public VoiceService Voice { get; }

    public static AppServices Open(string dataDir, ISecretStore? secrets = null)
    {
        secrets ??= OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new InMemorySecretStore();
        var dbDir = Path.Combine(dataDir, "data");
        Directory.CreateDirectory(dbDir);
        var factory = new SqliteDbFactory(Path.Combine(dbDir, "aiustoz.db"));
        var report = DatabaseMigrator.Migrate(factory, Path.Combine(dataDir, "backups"));
        return new AppServices(dataDir, factory, report, secrets);
    }
}

public sealed class SettingsService
{
    private readonly IDbFactory _factory;
    public SettingsService(IDbFactory factory) => _factory = factory;

    public string? Get(string key)
    {
        using var db = _factory.Create();
        return db.AppSettings.AsNoTracking().FirstOrDefault(s => s.Key == key)?.Value;
    }

    public void Set(string key, string value)
    {
        using var db = _factory.Create();
        var s = db.AppSettings.FirstOrDefault(x => x.Key == key);
        if (s is null) db.AppSettings.Add(new Domain.AppSetting { Key = key, Value = value });
        else s.Value = value;
        db.SaveChanges();
    }
}
