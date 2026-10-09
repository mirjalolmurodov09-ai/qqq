using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

/// <summary>Guruhlar, o'quvchilar va fanlar.</summary>
public sealed class AcademicService
{
    private readonly IDbFactory _factory;
    public AcademicService(IDbFactory factory) => _factory = factory;

    // ---------- Guruhlar ----------

    public List<Group> ListGroups(UserSession session, bool includeArchived = false)
    {
        using var db = _factory.Create();
        var q = db.Groups.AsNoTracking().AsQueryable();
        if (!includeArchived) q = q.Where(g => !g.IsArchived);
        if (session.Role == UserRole.Observer)
        {
            var allowed = db.ObserverGroupAccess.Where(a => a.UserId == session.UserId).Select(a => a.GroupId).ToList();
            q = q.Where(g => allowed.Contains(g.Id));
        }
        return q.OrderBy(g => g.Name).ToList();
    }

    public bool CanSeeGroup(UserSession session, int groupId)
    {
        if (session.Role != UserRole.Observer) return true;
        using var db = _factory.Create();
        return db.ObserverGroupAccess.Any(a => a.UserId == session.UserId && a.GroupId == groupId);
    }

    public Group SaveGroup(UserSession session, Group input)
    {
        session.Demand(Permission.EditGroupsAndStudents);
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new BusinessRuleException("Guruh nomi kiritilmagan.");
        using var db = _factory.Create();
        if (db.Groups.Any(g => g.Name == name && g.Id != input.Id))
            throw new BusinessRuleException($"\"{name}\" nomli guruh allaqachon mavjud.");
        Group g;
        if (input.Id == 0)
        {
            g = new Group();
            db.Groups.Add(g);
        }
        else g = db.Groups.Find(input.Id) ?? throw new BusinessRuleException("Guruh topilmadi.");
        g.Name = name;
        g.Course = string.IsNullOrWhiteSpace(input.Course) ? null : input.Course.Trim();
        g.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        g.IsArchived = input.IsArchived;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Guruh yaratildi" : "Guruh o'zgartirildi", nameof(Group), g.Id, g.Name);
        db.SaveChanges();
        return g;
    }

    // ---------- O'quvchilar ----------

    public List<Student> ListStudents(UserSession session, int groupId, bool includeInactive = false)
    {
        if (!CanSeeGroup(session, groupId)) throw new AccessDeniedException("Bu guruhni ko'rishga ruxsat yo'q.");
        using var db = _factory.Create();
        var q = db.Students.AsNoTracking().Where(s => s.GroupId == groupId);
        if (!includeInactive) q = q.Where(s => s.IsActive);
        return q.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ThenBy(s => s.MiddleName).ToList();
    }

    public Student SaveStudent(UserSession session, Student input)
    {
        session.Demand(Permission.EditGroupsAndStudents);
        if (string.IsNullOrWhiteSpace(input.LastName) || string.IsNullOrWhiteSpace(input.FirstName))
            throw new BusinessRuleException("Familiya va ism majburiy.");
        using var db = _factory.Create();
        if (!db.Groups.Any(g => g.Id == input.GroupId)) throw new BusinessRuleException("Guruh tanlanmagan.");
        var number = string.IsNullOrWhiteSpace(input.StudentNumber) ? null : input.StudentNumber.Trim();
        if (number is not null && db.Students.Any(s => s.StudentNumber == number && s.Id != input.Id))
            throw new BusinessRuleException($"O'quvchi raqami {number} boshqa o'quvchiga berilgan.");

        Student s;
        if (input.Id == 0) { s = new Student(); db.Students.Add(s); }
        else s = db.Students.Find(input.Id) ?? throw new BusinessRuleException("O'quvchi topilmadi.");
        s.LastName = input.LastName.Trim();
        s.FirstName = input.FirstName.Trim();
        s.MiddleName = string.IsNullOrWhiteSpace(input.MiddleName) ? null : input.MiddleName.Trim();
        s.StudentNumber = number;
        s.GroupId = input.GroupId;
        s.IsActive = input.IsActive;
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "O'quvchi qo'shildi" : "O'quvchi o'zgartirildi", nameof(Student), s.Id, s.FullName);
        db.SaveChanges();
        return s;
    }

    /// <summary>Shu guruhda xuddi shu F.I.Sh. li faol o'quvchi bormi (ogohlantirish uchun, bloklamaydi).</summary>
    public bool HasSameNameInGroup(int groupId, string last, string first, string? middle, int exceptId = 0)
    {
        using var db = _factory.Create();
        var key = StudentImport.NameKey(last, first, middle);
        return db.Students.AsNoTracking().Where(s => s.GroupId == groupId && s.Id != exceptId).ToList()
                 .Any(s => StudentImport.NameKey(s.LastName, s.FirstName, s.MiddleName) == key);
    }

    /// <summary>O'quvchini arxivlash (faolsizlantirish). Davomat tarixi saqlanadi.</summary>
    public void SetStudentActive(UserSession session, int studentId, bool active)
    {
        session.Demand(Permission.EditGroupsAndStudents);
        using var db = _factory.Create();
        var s = db.Students.Find(studentId) ?? throw new BusinessRuleException("O'quvchi topilmadi.");
        s.IsActive = active;
        db.Audit(session, active ? "O'quvchi faollashtirildi" : "O'quvchi arxivlandi", nameof(Student), s.Id, s.FullName);
        db.SaveChanges();
    }

    public (HashSet<string> Numbers, HashSet<string> NamesInGroup) GetDuplicateKeys(int groupId)
    {
        using var db = _factory.Create();
        var numbers = db.Students.AsNoTracking().Where(s => s.StudentNumber != null).Select(s => s.StudentNumber!).ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = db.Students.AsNoTracking().Where(s => s.GroupId == groupId).ToList()
            .Select(s => StudentImport.NameKey(s.LastName, s.FirstName, s.MiddleName)).ToHashSet();
        return (numbers, names);
    }

    /// <summary>Tekshirilgan import satrlarini bitta tranzaksiyada saqlaydi.</summary>
    public int ImportStudents(UserSession session, int groupId, IEnumerable<Student> students)
    {
        session.Demand(Permission.EditGroupsAndStudents);
        using var db = _factory.Create();
        if (!db.Groups.Any(g => g.Id == groupId)) throw new BusinessRuleException("Guruh topilmadi.");
        using var tx = db.Database.BeginTransaction();
        int n = 0;
        foreach (var s in students)
        {
            if (s.StudentNumber is not null && db.Students.Any(x => x.StudentNumber == s.StudentNumber))
                throw new BusinessRuleException($"O'quvchi raqami {s.StudentNumber} allaqachon mavjud. Import bekor qilindi.");
            db.Students.Add(new Student
            {
                LastName = s.LastName, FirstName = s.FirstName, MiddleName = s.MiddleName,
                StudentNumber = s.StudentNumber, GroupId = groupId, IsActive = true,
            });
            n++;
            db.SaveChanges();
        }
        db.Audit(session, "O'quvchilar import qilindi", nameof(Group), groupId, $"{n} ta o'quvchi");
        db.SaveChanges();
        tx.Commit();
        return n;
    }

    // ---------- Fanlar ----------

    public List<Subject> ListSubjects()
    {
        using var db = _factory.Create();
        return db.Subjects.AsNoTracking().OrderBy(s => s.Name).ToList();
    }

    public Subject SaveSubject(UserSession session, Subject input)
    {
        session.Demand(Permission.EditTimetable);
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0) throw new BusinessRuleException("Fan nomi kiritilmagan.");
        using var db = _factory.Create();
        if (db.Subjects.Any(s => s.Name == name && s.Id != input.Id))
            throw new BusinessRuleException($"\"{name}\" fani allaqachon mavjud.");
        Subject s;
        if (input.Id == 0) { s = new Subject(); db.Subjects.Add(s); }
        else s = db.Subjects.Find(input.Id) ?? throw new BusinessRuleException("Fan topilmadi.");
        s.Name = name;
        s.Code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Fan qo'shildi" : "Fan o'zgartirildi", nameof(Subject), s.Id, s.Name);
        db.SaveChanges();
        return s;
    }
}
