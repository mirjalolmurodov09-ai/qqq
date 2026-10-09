using AiUstozPro.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AiUstozPro.Infrastructure.Data;

/// <summary>
/// Ma'lumotlar bazasi sxemasini versiyalar bilan boshqaradi.
/// 1-versiya — boshlang'ich sxema (EF model asosida yaratiladi).
/// Keyingi versiyalar <see cref="Upgrades"/> ro'yxatiga SQL qadamlar sifatida qo'shiladi va
/// yangilashdan oldin avtomatik zaxira nusxa olinadi — mavjud yozuvlar saqlanadi.
/// </summary>
public static class DatabaseMigrator
{
    public const int CurrentVersion = 3;
    public const string VersionKey = "SchemaVersion";

    /// <summary>Versiya N-1 → N uchun yangilash qadamlari.</summary>
    private static readonly SortedDictionary<int, Action<AppDbContext>> Upgrades = new()
    {
        // v0.2: AI yordamchi suhbatlari.
        [2] = db => CreateMissingTables(db, "AiConversations", "AiMessages"),
        // v0.3: test va baholash.
        [3] = db => CreateMissingTables(db, "Assessments", "TestQuestions", "TestOptions", "TestResults", "TestAnswers"),
    };

    /// <summary>
    /// Model bo'yicha EF yaratadigan skriptdan faqat ko'rsatilgan jadvallarning CREATE TABLE / CREATE INDEX
    /// buyruqlarini ajratib bajaradi — shunday qilib qo'lda yozilgan SQL model bilan farq qilmaydi.
    /// Jadval allaqachon mavjud bo'lsa, o'tkazib yuboriladi.
    /// </summary>
    internal static void CreateMissingTables(AppDbContext db, params string[] tables)
    {
        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        foreach (var table in tables)
        {
            if (TableExists(db, table)) continue;
            var create = statements.FirstOrDefault(x => x.StartsWith($"CREATE TABLE \"{table}\"", StringComparison.Ordinal))
                         ?? throw new InvalidOperationException($"Modelda \"{table}\" jadvali topilmadi.");
            db.Database.ExecuteSqlRaw(create + ";");
            foreach (var idx in statements.Where(x => x.StartsWith("CREATE", StringComparison.Ordinal) && x.Contains("INDEX") && x.Contains($" ON \"{table}\" ", StringComparison.Ordinal)))
                db.Database.ExecuteSqlRaw(idx + ";");
        }
    }

    public static bool TableExists(AppDbContext db, string table)
    {
        var conn = db.Database.GetDbConnection();
        var wasClosed = conn.State != System.Data.ConnectionState.Open;
        if (wasClosed) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            if (db.Database.CurrentTransaction is { } tx) cmd.Transaction = tx.GetDbTransaction();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n;";
            var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = table; cmd.Parameters.Add(p);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        finally
        {
            if (wasClosed) conn.Close();
        }
    }

    public sealed record MigrationReport(int FromVersion, int ToVersion, bool Created, string? BackupPath);

    public static MigrationReport Migrate(IDbFactory factory, string? backupDir = null)
    {
        var existed = File.Exists(factory.DatabasePath) && new FileInfo(factory.DatabasePath).Length > 0;
        using var db = factory.Create();
        if (!existed)
        {
            db.Database.EnsureCreated();
            SetVersion(db, CurrentVersion);
            return new MigrationReport(0, CurrentVersion, true, null);
        }

        // Mavjud baza: jadval borligini tekshirish (EnsureCreated bo'sh fayl uchun ham ishlaydi).
        db.Database.EnsureCreated();
        var version = GetVersion(db);
        if (version == 0)
        {
            SetVersion(db, 1);
            version = 1;
        }
        if (version > CurrentVersion)
            throw new InvalidOperationException(
                $"Ma'lumotlar bazasi dasturning yangiroq versiyasida yaratilgan (sxema {version}). Dasturni yangilang.");
        if (version == CurrentVersion)
            return new MigrationReport(version, version, false, null);

        string? backupPath = null;
        if (backupDir is not null)
        {
            Directory.CreateDirectory(backupDir);
            backupPath = Path.Combine(backupDir, $"before-upgrade-v{version}-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            SqliteBackup(factory.DatabasePath, backupPath);
        }

        using var tx = db.Database.BeginTransaction();
        for (int v = version + 1; v <= CurrentVersion; v++)
        {
            if (Upgrades.TryGetValue(v, out var step)) step(db);
        }
        SetVersion(db, CurrentVersion);
        tx.Commit();
        return new MigrationReport(version, CurrentVersion, false, backupPath);
    }

    public static int GetVersion(AppDbContext db)
    {
        var s = db.AppSettings.AsNoTracking().FirstOrDefault(x => x.Key == VersionKey);
        return s is not null && int.TryParse(s.Value, out var v) ? v : 0;
    }

    private static void SetVersion(AppDbContext db, int version)
    {
        var s = db.AppSettings.FirstOrDefault(x => x.Key == VersionKey);
        if (s is null) db.AppSettings.Add(new AppSetting { Key = VersionKey, Value = version.ToString() });
        else s.Value = version.ToString();
        db.SaveChanges();
    }

    /// <summary>SQLite onlayn zaxira API orqali izchil nusxa (baza ochiq bo'lsa ham).</summary>
    public static void SqliteBackup(string sourcePath, string destPath)
    {
        if (File.Exists(destPath)) File.Delete(destPath);
        using var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destPath, Pooling = false }.ToString());
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
    }
}
