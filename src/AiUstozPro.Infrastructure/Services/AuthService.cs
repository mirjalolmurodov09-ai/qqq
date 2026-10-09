using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public static class AuditExtensions
{
    public static void Audit(this AppDbContext db, UserSession? session, string action, string entityType, int? entityId,
        string? details = null, string? reason = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = session?.UserId,
            UserLogin = session?.Login ?? "tizim",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Reason = reason,
        });
    }
}

public sealed class LoginResult
{
    public UserSession? Session { get; init; }
    public string? Error { get; init; }
    public bool Success => Session is not null;
}

public sealed class AuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IDbFactory _factory;
    public AuthService(IDbFactory factory) => _factory = factory;

    public bool HasAnyUser()
    {
        using var db = _factory.Create();
        return db.Users.Any();
    }

    /// <summary>Birinchi ishga tushirishda administrator yaratish (faqat foydalanuvchilar bo'lmasa).</summary>
    public UserSession CreateInitialAdministrator(string login, string fullName, string password)
    {
        using var db = _factory.Create();
        if (db.Users.Any()) throw new BusinessRuleException("Administrator allaqachon yaratilgan.");
        var user = NewUser(login, fullName, password, UserRole.Administrator);
        db.Users.Add(user);
        db.SaveChanges();
        var session = new UserSession(user.Id, user.Login, user.FullName, user.Role);
        db.Audit(session, "Birinchi administrator yaratildi", nameof(User), user.Id);
        db.SaveChanges();
        return session;
    }

    public LoginResult Login(string login, string password)
    {
        using var db = _factory.Create();
        var norm = (login ?? "").Trim().ToLowerInvariant();
        var user = db.Users.FirstOrDefault(u => u.Login == norm);
        if (user is null)
            return new LoginResult { Error = "Login yoki parol noto'g'ri." };
        if (user.IsBlocked)
            return new LoginResult { Error = "Foydalanuvchi bloklangan. Administratorga murojaat qiling." };
        if (user.LockedUntilUtc is { } until && until > DateTime.UtcNow)
            return new LoginResult { Error = $"Ko'p marta noto'g'ri parol kiritildi. {Math.Ceiling((until - DateTime.UtcNow).TotalMinutes)} daqiqadan so'ng urinib ko'ring." };

        if (!PasswordHasher.Verify(password ?? "", user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = DateTime.UtcNow + LockoutDuration;
                user.FailedLoginCount = 0;
            }
            db.Audit(null, "Muvaffaqiyatsiz kirish", nameof(User), user.Id, $"login={user.Login}");
            db.SaveChanges();
            return new LoginResult { Error = "Login yoki parol noto'g'ri." };
        }

        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        user.LastLoginUtc = DateTime.UtcNow;
        var session = new UserSession(user.Id, user.Login, user.FullName, user.Role);
        db.Audit(session, "Kirish", nameof(User), user.Id);
        db.SaveChanges();
        return new LoginResult { Session = session };
    }

    public List<User> ListUsers(UserSession session)
    {
        session.Demand(Permission.ManageUsers);
        using var db = _factory.Create();
        return db.Users.AsNoTracking().OrderBy(u => u.FullName).ToList();
    }

    /// <summary>O'qituvchilar ro'yxati (dars jadvali uchun) — har bir rol ko'ra oladi.</summary>
    public List<User> ListTeachers()
    {
        using var db = _factory.Create();
        return db.Users.AsNoTracking()
            .Where(u => !u.IsBlocked && (u.Role == UserRole.Teacher || u.Role == UserRole.Administrator))
            .OrderBy(u => u.FullName).ToList();
    }

    public User CreateUser(UserSession session, string login, string fullName, string password, UserRole role)
    {
        session.Demand(Permission.ManageUsers);
        using var db = _factory.Create();
        var user = NewUser(login, fullName, password, role);
        if (db.Users.Any(u => u.Login == user.Login))
            throw new BusinessRuleException($"\"{user.Login}\" logini band.");
        db.Users.Add(user);
        db.SaveChanges();
        db.Audit(session, "Foydalanuvchi yaratildi", nameof(User), user.Id, $"{user.Login} ({role.ToUz()})");
        db.SaveChanges();
        return user;
    }

    public void SetBlocked(UserSession session, int userId, bool blocked)
    {
        session.Demand(Permission.ManageUsers);
        if (userId == session.UserId && blocked) throw new BusinessRuleException("O'zingizni bloklay olmaysiz.");
        using var db = _factory.Create();
        var user = db.Users.Find(userId) ?? throw new BusinessRuleException("Foydalanuvchi topilmadi.");
        if (blocked && user.Role == UserRole.Administrator &&
            db.Users.Count(u => u.Role == UserRole.Administrator && !u.IsBlocked) <= 1)
            throw new BusinessRuleException("Oxirgi faol administratorni bloklab bo'lmaydi.");
        user.IsBlocked = blocked;
        db.Audit(session, blocked ? "Foydalanuvchi bloklandi" : "Foydalanuvchi blokdan chiqarildi", nameof(User), userId, user.Login);
        db.SaveChanges();
    }

    public void ResetPassword(UserSession session, int userId, string newPassword)
    {
        if (userId != session.UserId) session.Demand(Permission.ManageUsers);
        var err = PasswordHasher.ValidateStrength(newPassword);
        if (err is not null) throw new BusinessRuleException(err);
        using var db = _factory.Create();
        var user = db.Users.Find(userId) ?? throw new BusinessRuleException("Foydalanuvchi topilmadi.");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        db.Audit(session, "Parol o'zgartirildi", nameof(User), userId, user.Login);
        db.SaveChanges();
    }

    public void SetObserverGroups(UserSession session, int userId, IEnumerable<int> groupIds)
    {
        session.Demand(Permission.ManageUsers);
        using var db = _factory.Create();
        var existing = db.ObserverGroupAccess.Where(a => a.UserId == userId).ToList();
        db.ObserverGroupAccess.RemoveRange(existing);
        foreach (var gid in groupIds.Distinct())
            db.ObserverGroupAccess.Add(new ObserverGroupAccess { UserId = userId, GroupId = gid });
        db.Audit(session, "Kuzatuvchi guruhlari o'zgartirildi", nameof(User), userId);
        db.SaveChanges();
    }

    public List<int> GetObserverGroups(int userId)
    {
        using var db = _factory.Create();
        return db.ObserverGroupAccess.Where(a => a.UserId == userId).Select(a => a.GroupId).ToList();
    }

    private static User NewUser(string login, string fullName, string password, UserRole role)
    {
        var norm = (login ?? "").Trim().ToLowerInvariant();
        if (norm.Length < 3 || !norm.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new BusinessRuleException("Login kamida 3 belgi bo'lsin va faqat lotin harflari, raqamlar, '.', '_' yoki '-' dan iborat bo'lsin.");
        if (string.IsNullOrWhiteSpace(fullName)) throw new BusinessRuleException("F.I.Sh. kiritilmagan.");
        var err = PasswordHasher.ValidateStrength(password);
        if (err is not null) throw new BusinessRuleException(err);
        return new User
        {
            Login = norm,
            FullName = fullName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
        };
    }
}
