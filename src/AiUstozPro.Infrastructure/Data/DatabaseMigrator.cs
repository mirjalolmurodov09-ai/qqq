using AiUstozPro.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Data;

/// <summary>
/// Ma'lumotlar bazasi sxemasini versiyalar bilan boshqaradi.
/// 1-versiya — boshlang'ich sxema (EF model asosida yaratiladi).
/// Keyingi versiyalar <see cref="Upgrades"/> ro'yxatiga SQL qadamlar sifatida qo'shiladi va
/// yangilashdan oldin avtomatik zaxira nusxa olinadi — mavjud yozuvlar saqlanadi.
/// </summary>
public static class DatabaseMigrator
{
    public const int CurrentVersion = 1;
    public const string VersionKey = "SchemaVersion";

    /// <summary>Versiya N → N+1 uchun SQL buyruqlar. Hozircha bo'sh (v0.1 boshlang'ich sxema).</summary>
    private static readonly SortedDictionary<int, string[]> Upgrades = new()
    {
        // [2] = new[] { "ALTER TABLE Students ADD COLUMN PhotoPath TEXT NULL;" },
    };

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
            if (Upgrades.TryGetValue(v, out var steps))
                foreach (var sql in steps) db.Database.ExecuteSqlRaw(sql);
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
