using System.Security.Cryptography;
using AiUstozPro.Domain;

namespace AiUstozPro.Application.Security;

/// <summary>PBKDF2-HMAC-SHA256 (tasodifiy tuz, 210 000 iteratsiya). Format: v1$iter$salt$hash (base64).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored) || password is null) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iter)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Parol talablari: kamida 8 belgi, harf va raqam.</summary>
    public static string? ValidateStrength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return "Parol kamida 8 belgidan iborat bo'lishi kerak.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Parolda kamida bitta harf va bitta raqam bo'lishi kerak.";
        return null;
    }
}

public enum Permission
{
    ManageUsers,
    ManageSettings,
    ManageBackups,
    ViewAuditLog,
    EditCalendar,
    EditTimetable,
    EditGroupsAndStudents,
    EditCurriculum,
    TakeAttendance,
    ExportReports,
    ViewReports,
    UseAi,
    ManageTests,
}

public static class Permissions
{
    public static bool Has(UserRole role, Permission p) => role switch
    {
        UserRole.Administrator => true,
        UserRole.Teacher => p is Permission.EditTimetable or Permission.EditGroupsAndStudents
                              or Permission.EditCurriculum or Permission.TakeAttendance
                              or Permission.ExportReports or Permission.ViewReports or Permission.EditCalendar
                              or Permission.UseAi or Permission.ManageTests,
        UserRole.Observer => p is Permission.ViewReports or Permission.ExportReports,
        _ => false,
    };
}

/// <summary>Joriy foydalanuvchi sessiyasi.</summary>
public sealed class UserSession
{
    public UserSession(int userId, string login, string fullName, UserRole role)
    {
        UserId = userId; Login = login; FullName = fullName; Role = role;
        StartedUtc = DateTime.UtcNow;
    }

    public int UserId { get; }
    public string Login { get; }
    public string FullName { get; }
    public UserRole Role { get; }
    public DateTime StartedUtc { get; }

    public bool Can(Permission p) => Permissions.Has(Role, p);

    public void Demand(Permission p)
    {
        if (!Can(p)) throw new AccessDeniedException($"Bu amal uchun ruxsat yo'q ({Role.ToUz()}).");
    }
}

public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException(string message) : base(message) { }
}

/// <summary>Foydalanuvchiga tushunarli xabar bilan rad etilgan amal.</summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
