using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

/// <summary>Zaxira nusxa yaratish va tiklash.</summary>
public sealed class BackupService
{
    private readonly IDbFactory _factory;
    private readonly string _backupDir;

    public BackupService(IDbFactory factory, string backupDir)
    {
        _factory = factory;
        _backupDir = backupDir;
    }

    public string BackupDirectory => _backupDir;

    public BackupHistory CreateBackup(UserSession? session, string kind = "manual", string? destinationPath = null)
    {
        if (session is not null && kind == "manual") session.Demand(Permission.ManageBackups);
        Directory.CreateDirectory(_backupDir);
        var path = destinationPath ?? Path.Combine(_backupDir, $"aiustoz-{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.db");
        DatabaseMigrator.SqliteBackup(_factory.DatabasePath, path);
        if (!IsValidBackup(path, out var err)) throw new BusinessRuleException($"Zaxira nusxa tekshiruvdan o'tmadi: {err}");
        var info = new FileInfo(path);
        using var db = _factory.Create();
        var h = new BackupHistory { FilePath = path, SizeBytes = info.Length, Kind = kind, CreatedBy = session?.Login ?? "tizim" };
        db.BackupHistory.Add(h);
        db.Audit(session, "Zaxira nusxa yaratildi", nameof(BackupHistory), null, path);
        db.SaveChanges();
        return h;
    }

    /// <summary>Parol bilan shifrlangan nusxa (fleshka, tarmoq disk yoki boshqa joy uchun).</summary>
    public BackupHistory ExportEncrypted(UserSession session, string destinationPath, string password)
    {
        session.Demand(Permission.ManageBackups);
        Directory.CreateDirectory(_backupDir);
        var tmp = Path.Combine(_backupDir, $"~export-{Guid.NewGuid():N}.db");
        try
        {
            DatabaseMigrator.SqliteBackup(_factory.DatabasePath, tmp);
            if (!IsValidBackup(tmp, out var err)) throw new BusinessRuleException($"Zaxira nusxa tekshiruvdan o'tmadi: {err}");
            var dir = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            BackupCrypto.EncryptFile(tmp, destinationPath, password);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        using var db = _factory.Create();
        var h = new BackupHistory { FilePath = destinationPath, SizeBytes = new FileInfo(destinationPath).Length, Kind = "encrypted", CreatedBy = session.Login };
        db.BackupHistory.Add(h);
        db.Audit(session, "Shifrlangan zaxira nusxa yaratildi", nameof(BackupHistory), null, destinationPath);
        db.SaveChanges();
        return h;
    }

    /// <summary>Kuniga bir martadan ko'p bo'lmagan avtomatik zaxira; eng so'nggi 14 ta avtomatik nusxa saqlanadi.</summary>
    public BackupHistory? AutoBackupIfDue()
    {
        Directory.CreateDirectory(_backupDir);
        var today = DateTime.Now.ToString("yyyyMMdd");
        var autos = Directory.GetFiles(_backupDir, "aiustoz-*-auto.db").OrderByDescending(f => f).ToList();
        if (autos.Any(f => Path.GetFileName(f).StartsWith($"aiustoz-{today}"))) return null;
        var h = CreateBackup(null, "auto");
        foreach (var old in Directory.GetFiles(_backupDir, "aiustoz-*-auto.db").OrderByDescending(f => f).Skip(14))
        {
            try { File.Delete(old); } catch (IOException) { /* keyingi safar */ }
        }
        return h;
    }

    public List<BackupHistory> ListHistory()
    {
        using var db = _factory.Create();
        return db.BackupHistory.AsNoTracking().OrderByDescending(b => b.CreatedUtc).Take(200).ToList();
    }

    public static bool IsValidBackup(string path, out string? error)
    {
        error = null;
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var res = cmd.ExecuteScalar() as string;
                if (res != "ok") { error = $"butunlik tekshiruvi: {res}"; return false; }
            }
            foreach (var table in new[] { "Users", "Groups", "Students", "LessonOccurrences", "AppSettings" })
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n;";
                cmd.Parameters.AddWithValue("$n", table);
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) { error = $"\"{table}\" jadvali yo'q — bu AI Ustoz Pro bazasi emas."; return false; }
            }
            return true;
        }
        catch (SqliteException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Zaxira nusxadan tiklash. Avval joriy baza "before-restore" nusxasi sifatida saqlanadi.
    /// Tiklangandan keyin dasturni qayta ishga tushirish kerak.
    /// </summary>
    public string Restore(UserSession session, string backupPath, string? password = null)
    {
        session.Demand(Permission.ManageBackups);
        if (!File.Exists(backupPath)) throw new BusinessRuleException("Fayl topilmadi.");
        if (BackupCrypto.IsEncrypted(backupPath))
        {
            if (string.IsNullOrEmpty(password)) throw new BusinessRuleException("Bu zaxira nusxa shifrlangan — parolni kiriting.");
            Directory.CreateDirectory(_backupDir);
            var tmp = Path.Combine(_backupDir, $"~restore-{Guid.NewGuid():N}.db");
            try
            {
                BackupCrypto.DecryptFile(backupPath, tmp, password);
                return Restore(session, tmp, null);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }
        if (!IsValidBackup(backupPath, out var err)) throw new BusinessRuleException($"Bu fayldan tiklab bo'lmaydi: {err}");

        var safety = CreateBackup(session, "before-restore");
        using (var db = _factory.Create())
        {
            db.Audit(session, "Zaxira nusxadan tiklash boshlandi", nameof(BackupHistory), null, backupPath);
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
        // Tiklash: zaxira faylini SQLite backup API orqali joriy bazaga yozamiz (fayl qulflari xavfsiz).
        using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _factory.DatabasePath, Pooling = false }.ToString()))
        {
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }
        SqliteConnection.ClearAllPools();
        // Tiklangan bazaning sxemasini tekshirish/yangilash.
        DatabaseMigrator.Migrate(_factory, _backupDir);
        using (var db = _factory.Create())
        {
            db.Audit(session, "Zaxira nusxadan tiklandi", nameof(BackupHistory), null, $"{backupPath}; oldingi holat: {safety.FilePath}");
            db.SaveChanges();
        }
        return safety.FilePath;
    }
}
