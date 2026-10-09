using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Application.Testing;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Data;
using AiUstozPro.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace AiUstozPro.Infrastructure.Services;

public sealed record OptionInput(string Text, bool IsCorrect);

public sealed record QuestionInput(int Id, string Text, int Points, IReadOnlyList<OptionInput> Options);

public sealed class ResultRow
{
    public required Student Student { get; init; }
    public TestResult? Result { get; init; }
    public string Answers { get; init; } = "";
}

public sealed record ImportResultRow(int RowNumber, Student? Student, int Variant, string Answers, string? Error);

/// <summary>Testlar, savollar, variantlar, natijalar va baholash.</summary>
public sealed class AssessmentService
{
    public const int MaxVariants = 8;
    private readonly IDbFactory _factory;
    private readonly Func<DateTime> _utcNow;

    public AssessmentService(IDbFactory factory, Func<DateTime>? utcNow = null)
    {
        _factory = factory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    // ---------- Testlar ----------

    public List<Assessment> ListAssessments(UserSession session, int? subjectId = null, bool includeArchived = false)
    {
        session.Demand(Permission.ViewReports);
        using var db = _factory.Create();
        var q = db.Assessments.AsNoTracking().Include(a => a.Subject).AsQueryable();
        if (subjectId is int s) q = q.Where(a => a.SubjectId == s);
        if (!includeArchived) q = q.Where(a => a.Status != AssessmentStatus.Archived);
        return q.OrderByDescending(a => a.UpdatedUtc).ToList();
    }

    public Assessment Get(int id)
    {
        using var db = _factory.Create();
        var a = db.Assessments.AsNoTracking().Include(x => x.Subject).Include(x => x.Questions).ThenInclude(q => q.Options)
                  .AsSplitQuery().FirstOrDefault(x => x.Id == id) ?? throw new BusinessRuleException("Test topilmadi.");
        a.Questions = a.Questions.OrderBy(q => q.OrderNo).ThenBy(q => q.Id).ToList();
        foreach (var q in a.Questions) q.Options = q.Options.OrderBy(o => o.OrderNo).ThenBy(o => o.Id).ToList();
        return a;
    }

    public bool HasResults(int assessmentId)
    {
        using var db = _factory.Create();
        return db.TestResults.Any(r => r.AssessmentId == assessmentId);
    }

    public Assessment Save(UserSession session, Assessment input)
    {
        session.Demand(Permission.ManageTests);
        if (string.IsNullOrWhiteSpace(input.Title)) throw new BusinessRuleException("Test nomini kiriting.");
        if (input.TimeLimitMinutes < 0 || input.TimeLimitMinutes > 300) throw new BusinessRuleException("Vaqt 0 dan 300 daqiqagacha (0 — cheklanmagan).");
        if (input.VariantCount < 1 || input.VariantCount > MaxVariants) throw new BusinessRuleException($"Variantlar soni 1 dan {MaxVariants} gacha.");
        var scale = new GradingScale(input.Grade5Min, input.Grade4Min, input.Grade3Min);
        var err = scale.Validate();
        if (err is not null) throw new BusinessRuleException(err);
        using var db = _factory.Create();
        if (!db.Subjects.Any(s => s.Id == input.SubjectId)) throw new BusinessRuleException("Fan tanlanmagan.");
        Assessment a;
        bool scaleChanged = false;
        if (input.Id == 0)
        {
            a = new Assessment { CreatedByUserId = session.UserId, CreatedUtc = _utcNow(), Status = AssessmentStatus.Draft };
            db.Assessments.Add(a);
        }
        else
        {
            a = db.Assessments.Find(input.Id) ?? throw new BusinessRuleException("Test topilmadi.");
            var hasResults = db.TestResults.Any(r => r.AssessmentId == a.Id);
            if (hasResults && (a.ShuffleQuestions != input.ShuffleQuestions || a.ShuffleOptions != input.ShuffleOptions || input.VariantCount < a.VariantCount))
                throw new BusinessRuleException("Natijalar mavjud — variantlarni aralashtirish sozlamalarini o'zgartirib bo'lmaydi (javoblar mos kelmay qoladi). Testdan nusxa oling.");
            scaleChanged = a.Grade5Min != input.Grade5Min || a.Grade4Min != input.Grade4Min || a.Grade3Min != input.Grade3Min;
        }
        a.Title = input.Title.Trim();
        a.SubjectId = input.SubjectId;
        a.CurriculumTopicId = input.CurriculumTopicId;
        a.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        a.TimeLimitMinutes = input.TimeLimitMinutes;
        a.ShuffleQuestions = input.ShuffleQuestions;
        a.ShuffleOptions = input.ShuffleOptions;
        a.VariantCount = input.VariantCount;
        a.Grade5Min = input.Grade5Min; a.Grade4Min = input.Grade4Min; a.Grade3Min = input.Grade3Min;
        a.UpdatedUtc = _utcNow();
        db.SaveChanges();
        if (scaleChanged)
        {
            // Mezon o'zgarsa — tasdiqlanmagan natijalarning avtomatik bahosi qayta hisoblanadi; tasdiqlanganlar o'zgarmaydi.
            foreach (var r in db.TestResults.Where(r => r.AssessmentId == a.Id && !r.IsConfirmed))
                r.AutoGrade = scale.Grade(r.Percent);
        }
        db.Audit(session, input.Id == 0 ? "Test yaratildi" : "Test o'zgartirildi", nameof(Assessment), a.Id, $"{a.Title}; {scale.Describe()}");
        db.SaveChanges();
        return a;
    }

    /// <summary>Testdan nusxa (savollar bilan, natijalarsiz) — natijalari bor testni o'zgartirish uchun.</summary>
    public Assessment Duplicate(UserSession session, int assessmentId)
    {
        session.Demand(Permission.ManageTests);
        var src = Get(assessmentId);
        using var db = _factory.Create();
        var copy = new Assessment
        {
            Title = src.Title + " (nusxa)", SubjectId = src.SubjectId, CurriculumTopicId = src.CurriculumTopicId, Description = src.Description,
            TimeLimitMinutes = src.TimeLimitMinutes, ShuffleQuestions = src.ShuffleQuestions, ShuffleOptions = src.ShuffleOptions,
            VariantCount = src.VariantCount, Grade5Min = src.Grade5Min, Grade4Min = src.Grade4Min, Grade3Min = src.Grade3Min,
            Status = AssessmentStatus.Draft, CreatedByUserId = session.UserId, CreatedUtc = _utcNow(), UpdatedUtc = _utcNow(),
            Questions = src.Questions.Select(q => new TestQuestion
            {
                OrderNo = q.OrderNo, Text = q.Text, Points = q.Points, Source = q.Source, IsApproved = q.IsApproved,
                Options = q.Options.Select(o => new TestOption { OrderNo = o.OrderNo, Text = o.Text, IsCorrect = o.IsCorrect }).ToList(),
            }).ToList(),
        };
        db.Assessments.Add(copy);
        db.SaveChanges();
        db.Audit(session, "Testdan nusxa olindi", nameof(Assessment), copy.Id, $"manba #{assessmentId}");
        db.SaveChanges();
        return copy;
    }

    public void SetStatus(UserSession session, int assessmentId, AssessmentStatus status)
    {
        session.Demand(Permission.ManageTests);
        using var db = _factory.Create();
        var a = db.Assessments.Find(assessmentId) ?? throw new BusinessRuleException("Test topilmadi.");
        if (status == AssessmentStatus.Ready)
        {
            var problems = ValidateForUse(db, assessmentId);
            if (problems.Count > 0) throw new BusinessRuleException("Test hali tayyor emas:\n• " + string.Join("\n• ", problems));
        }
        a.Status = status;
        a.UpdatedUtc = _utcNow();
        db.Audit(session, "Test holati o'zgartirildi", nameof(Assessment), a.Id, status.ToUz());
        db.SaveChanges();
    }

    private static List<string> ValidateForUse(AppDbContext db, int assessmentId)
    {
        var problems = new List<string>();
        var qs = db.TestQuestions.AsNoTracking().Include(q => q.Options).Where(q => q.AssessmentId == assessmentId).ToList();
        if (qs.Count == 0) problems.Add("savollar yo'q");
        var unapproved = qs.Count(q => !q.IsApproved);
        if (unapproved > 0) problems.Add($"{unapproved} ta AI yaratgan savol o'qituvchi tomonidan tasdiqlanmagan");
        foreach (var q in qs)
        {
            if (q.Options.Count < 2) problems.Add($"{q.OrderNo}-savolda variantlar kam");
            if (q.Options.Count(o => o.IsCorrect) != 1) problems.Add($"{q.OrderNo}-savolda to'g'ri javob bitta bo'lishi kerak");
        }
        return problems;
    }

    // ---------- Savollar ----------

    private static void EnsureEditable(AppDbContext db, int assessmentId)
    {
        if (db.TestResults.Any(r => r.AssessmentId == assessmentId))
            throw new BusinessRuleException("Bu test bo'yicha natijalar mavjud — savollarni o'zgartirib bo'lmaydi (eski natijalar buziladi). \"Nusxa olish\" orqali yangi versiya yarating.");
    }

    private static void ValidateQuestion(QuestionInput q)
    {
        if (string.IsNullOrWhiteSpace(q.Text)) throw new BusinessRuleException("Savol matnini kiriting.");
        if (q.Points < 1 || q.Points > 20) throw new BusinessRuleException("Ball 1 dan 20 gacha.");
        var opts = q.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();
        if (opts.Count < 2 || opts.Count > 8) throw new BusinessRuleException("Variantlar soni 2 dan 8 gacha bo'lsin.");
        if (opts.Count(o => o.IsCorrect) != 1) throw new BusinessRuleException("To'g'ri javob aynan bitta bo'lishi kerak.");
        if (opts.Select(o => o.Text.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != opts.Count)
            throw new BusinessRuleException("Bir xil matnli variantlar bor.");
    }

    public TestQuestion SaveQuestion(UserSession session, int assessmentId, QuestionInput input, string source = "manual")
    {
        session.Demand(Permission.ManageTests);
        ValidateQuestion(input);
        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        EnsureEditable(db, assessmentId);
        var a = db.Assessments.Find(assessmentId) ?? throw new BusinessRuleException("Test topilmadi.");
        TestQuestion q;
        if (input.Id == 0)
        {
            q = new TestQuestion
            {
                AssessmentId = assessmentId, Source = source, IsApproved = source != "ai",
                OrderNo = (db.TestQuestions.Where(x => x.AssessmentId == assessmentId).Max(x => (int?)x.OrderNo) ?? 0) + 1,
            };
            db.TestQuestions.Add(q);
        }
        else
        {
            q = db.TestQuestions.Include(x => x.Options).FirstOrDefault(x => x.Id == input.Id && x.AssessmentId == assessmentId)
                ?? throw new BusinessRuleException("Savol topilmadi.");
            db.TestOptions.RemoveRange(q.Options);
            q.Options.Clear();
        }
        q.Text = input.Text.Trim();
        q.Points = input.Points;
        int i = 0;
        foreach (var o in input.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)))
            q.Options.Add(new TestOption { OrderNo = ++i, Text = o.Text.Trim(), IsCorrect = o.IsCorrect });
        if (a.Status == AssessmentStatus.Ready && !q.IsApproved) a.Status = AssessmentStatus.Draft;
        a.UpdatedUtc = _utcNow();
        db.SaveChanges();
        db.Audit(session, input.Id == 0 ? "Savol qo'shildi" : "Savol o'zgartirildi", nameof(TestQuestion), q.Id, Short(q.Text));
        db.SaveChanges();
        tx.Commit();
        return q;
    }

    /// <summary>AI yaratgan savollarni tasdiqlanmagan holatda qo'shadi.</summary>
    public int AddAiQuestions(UserSession session, int assessmentId, IEnumerable<ParsedQuestion> questions)
    {
        session.Demand(Permission.ManageTests);
        int n = 0;
        foreach (var p in questions)
        {
            SaveQuestion(session, assessmentId, new QuestionInput(0, p.Text, 1,
                p.Options.Select((o, i) => new OptionInput(o, i == p.CorrectIndex)).ToList()), "ai");
            n++;
        }
        using var db = _factory.Create();
        var a = db.Assessments.Find(assessmentId);
        if (a is not null && a.Status == AssessmentStatus.Ready) { a.Status = AssessmentStatus.Draft; db.SaveChanges(); }
        return n;
    }

    public void ApproveQuestion(UserSession session, int questionId)
    {
        session.Demand(Permission.ManageTests);
        using var db = _factory.Create();
        var q = db.TestQuestions.Find(questionId) ?? throw new BusinessRuleException("Savol topilmadi.");
        q.IsApproved = true;
        db.Audit(session, "AI savoli o'qituvchi tomonidan tasdiqlandi", nameof(TestQuestion), q.Id, Short(q.Text));
        db.SaveChanges();
    }

    public void DeleteQuestion(UserSession session, int questionId)
    {
        session.Demand(Permission.ManageTests);
        using var db = _factory.Create();
        var q = db.TestQuestions.Find(questionId) ?? throw new BusinessRuleException("Savol topilmadi.");
        EnsureEditable(db, q.AssessmentId);
        db.TestQuestions.Remove(q);
        db.SaveChanges();
        int i = 0;
        foreach (var x in db.TestQuestions.Where(x => x.AssessmentId == q.AssessmentId).OrderBy(x => x.OrderNo).ThenBy(x => x.Id)) x.OrderNo = ++i;
        db.Audit(session, "Savol o'chirildi", nameof(TestQuestion), questionId, Short(q.Text));
        db.SaveChanges();
    }

    public void MoveQuestion(UserSession session, int questionId, int delta)
    {
        session.Demand(Permission.ManageTests);
        using var db = _factory.Create();
        var q = db.TestQuestions.Find(questionId) ?? throw new BusinessRuleException("Savol topilmadi.");
        EnsureEditable(db, q.AssessmentId);
        var list = db.TestQuestions.Where(x => x.AssessmentId == q.AssessmentId).OrderBy(x => x.OrderNo).ThenBy(x => x.Id).ToList();
        var idx = list.FindIndex(x => x.Id == questionId);
        var to = Math.Clamp(idx + delta, 0, list.Count - 1);
        if (to == idx) return;
        list.RemoveAt(idx);
        list.Insert(to, q);
        for (int i = 0; i < list.Count; i++) list[i].OrderNo = i + 1;
        db.SaveChanges();
    }

    // ---------- Variantlar ----------

    public (TestVariant Variant, Dictionary<int, QuestionKey> Keys, Assessment Assessment) GetVariant(int assessmentId, int variant)
    {
        var a = Get(assessmentId);
        if (variant < 1 || variant > a.VariantCount) throw new BusinessRuleException($"Variant raqami 1 dan {a.VariantCount} gacha.");
        var keys = a.Questions.Where(q => q.IsApproved).Select(q => new QuestionKey(q.Id, q.Points, q.Options.Select(o => o.Id).ToList(),
            q.Options.FirstOrDefault(o => o.IsCorrect)?.Id ?? -1)).ToList();
        var v = VariantBuilder.Build(a.Id, variant, keys, a.ShuffleQuestions, a.ShuffleOptions);
        return (v, keys.ToDictionary(k => k.QuestionId), a);
    }

    /// <summary>Chop etish uchun test matni (har bir variant) — Word/PDF ga chiqariladi.</summary>
    public string PrintableText(int assessmentId, int variant, bool forStudents = true)
    {
        var (v, _, a) = GetVariant(assessmentId, variant);
        var byId = a.Questions.ToDictionary(q => q.Id);
        var opts = a.Questions.SelectMany(q => q.Options).ToDictionary(o => o.Id);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {a.Title} — {variant}-variant");
        sb.AppendLine($"Fan: {a.Subject?.Name}" + (a.TimeLimitMinutes > 0 ? $"   Vaqt: {a.TimeLimitMinutes} daqiqa" : ""));
        if (forStudents) sb.AppendLine("F.I.Sh.: ______________________________   Guruh: __________   Sana: ____________");
        sb.AppendLine();
        int n = 0;
        foreach (var vq in v.Questions)
        {
            var q = byId[vq.QuestionId];
            sb.AppendLine($"**{++n}. {q.Text}**" + (q.Points > 1 ? $" ({q.Points} ball)" : ""));
            for (int i = 0; i < vq.OptionIds.Count; i++) sb.AppendLine($"    {AnswerSheet.Letter(i)}) {opts[vq.OptionIds[i]].Text}");
            sb.AppendLine();
        }
        if (forStudents)
        {
            sb.AppendLine("# Javoblar varaqasi");
            sb.AppendLine(string.Join("   ", Enumerable.Range(1, v.Questions.Count).Select(i => $"{i}) ___")));
        }
        return sb.ToString();
    }

    /// <summary>Barcha variantlar uchun javoblar kaliti.</summary>
    public ReportTable AnswerKeys(int assessmentId)
    {
        var a = Get(assessmentId);
        var t = new ReportTable { Title = $"Javoblar kaliti: {a.Title}" };
        t.SubtitleLines.Add($"Fan: {a.Subject?.Name}. Faqat o'qituvchi uchun.");
        t.Columns.AddRange(new[] { "Variant", "To'g'ri javoblar" });
        t.Widths.AddRange(new[] { 1f, 8f });
        for (int v = 1; v <= a.VariantCount; v++)
        {
            var (variant, keys, _) = GetVariant(assessmentId, v);
            var key = variant.AnswerKey(keys);
            t.Rows.Add(new[] { v.ToString(), string.Join(" ", key.Select((c, i) => $"{i + 1}{c}")) });
        }
        return t;
    }

    // ---------- Natijalar ----------

    private static void DemandGroupForView(AppDbContext db, UserSession session, int groupId)
    {
        session.Demand(Permission.ViewReports);
        if (session.Role == UserRole.Observer && !db.ObserverGroupAccess.Any(x => x.UserId == session.UserId && x.GroupId == groupId))
            throw new AccessDeniedException("Bu guruh natijalarini ko'rishga ruxsat yo'q.");
    }

    /// <summary>Guruh o'quvchilari va ularning eng so'nggi urinishi.</summary>
    public List<ResultRow> ListResults(UserSession session, int assessmentId, int groupId)
    {
        using var db = _factory.Create();
        DemandGroupForView(db, session, groupId);
        var results = db.TestResults.AsNoTracking().Include(r => r.Answers)
            .Where(r => r.AssessmentId == assessmentId && r.GroupId == groupId).ToList()
            .GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AttemptNo).First());
        var resultStudentIds = results.Keys.ToList();
        var students = db.Students.AsNoTracking().Where(s => s.GroupId == groupId && (s.IsActive || resultStudentIds.Contains(s.Id)))
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
        var rows = new List<ResultRow>();
        var variants = new Dictionary<int, TestVariant>();
        foreach (var s in students)
        {
            results.TryGetValue(s.Id, out var r);
            var letters = "";
            if (r is not null)
            {
                if (!variants.TryGetValue(r.Variant, out var v)) variants[r.Variant] = v = GetVariant(assessmentId, r.Variant).Variant;
                var byQ = r.Answers.ToDictionary(x => x.QuestionId);
                letters = AnswerSheet.Format(v.Questions.Select(q =>
                    byQ.TryGetValue(q.QuestionId, out var ans) && ans.SelectedOptionId is int oid ? q.OptionIds.ToList().IndexOf(oid) : (int?)null));
            }
            rows.Add(new ResultRow { Student = s, Result = r, Answers = letters });
        }
        return rows;
    }

    /// <summary>
    /// Natijani yozadi (qo'lda kiritish, kompyuterda topshirish yoki import). Tasdiqlangan natija ustidan yozilmaydi —
    /// buning uchun newAttempt=true (qayta topshirish) yoki avval tasdiqni bekor qilish kerak.
    /// </summary>
    public TestResult RecordResult(UserSession session, int assessmentId, int studentId, DateOnly date, int variant,
        IReadOnlyList<int?> selections, ResultMethod method, int? durationSeconds = null, bool newAttempt = false, string? note = null)
    {
        session.Demand(Permission.ManageTests);
        var (v, keys, a) = GetVariant(assessmentId, variant);
        if (a.Status != AssessmentStatus.Ready) throw new BusinessRuleException("Test \"Tayyor\" holatida emas. Avval savollarni tekshirib, testni tayyor deb belgilang.");
        if (v.Questions.Count == 0) throw new BusinessRuleException("Testda tasdiqlangan savollar yo'q.");
        var scored = Scorer.Score(v, keys, selections);
        var scale = new GradingScale(a.Grade5Min, a.Grade4Min, a.Grade3Min);

        using var db = _factory.Create();
        using var tx = db.Database.BeginTransaction();
        var student = db.Students.Find(studentId) ?? throw new BusinessRuleException("O'quvchi topilmadi.");
        var existing = db.TestResults.Include(r => r.Answers).Where(r => r.AssessmentId == assessmentId && r.StudentId == studentId)
            .OrderByDescending(r => r.AttemptNo).FirstOrDefault();
        TestResult r;
        string action;
        if (existing is null || newAttempt)
        {
            r = new TestResult
            {
                AssessmentId = assessmentId, StudentId = studentId, GroupId = student.GroupId,
                AttemptNo = (existing?.AttemptNo ?? 0) + 1, CreatedUtc = _utcNow(),
            };
            db.TestResults.Add(r);
            action = r.AttemptNo > 1 ? "Test qayta topshirildi" : "Test natijasi kiritildi";
        }
        else
        {
            if (existing.IsConfirmed)
                throw new BusinessRuleException($"{student.FullName}: natija tasdiqlangan. O'zgartirish uchun avval tasdiqni bekor qiling yoki \"qayta topshirish\" sifatida kiriting.");
            r = existing;
            db.TestAnswers.RemoveRange(r.Answers);
            r.Answers.Clear();
            action = "Test natijasi o'zgartirildi";
        }
        r.Date = date; r.Variant = variant; r.Method = method; r.DurationSeconds = durationSeconds;
        r.SubmittedUtc = _utcNow();
        r.StartedUtc = durationSeconds is int d ? r.SubmittedUtc.Value.AddSeconds(-d) : null;
        r.Score = scored.Score; r.MaxScore = scored.MaxScore; r.Percent = scored.Percent;
        r.AutoGrade = scale.Grade(scored.Percent);
        r.FinalGrade = null; r.IsConfirmed = false; r.ConfirmedByUserId = null; r.ConfirmedUtc = null;
        r.Note = note; r.RecordedByUserId = session.UserId;
        foreach (var ans in scored.Answers)
            r.Answers.Add(new TestAnswer { QuestionId = ans.QuestionId, SelectedOptionId = ans.SelectedOptionId, IsCorrect = ans.IsCorrect, Points = ans.Points });
        db.SaveChanges();
        db.Audit(session, action, nameof(TestResult), r.Id,
            $"{student.FullName}: {r.Score}/{r.MaxScore} ({r.Percent:0.#}%), avtomatik baho {r.AutoGrade}, {method.ToUz()}");
        db.SaveChanges();
        tx.Commit();
        return r;
    }

    /// <summary>O'qituvchi yakuniy bahoni tasdiqlaydi. Avtomatik bahodan farq qilsa — sabab majburiy.</summary>
    public void Confirm(UserSession session, int resultId, int finalGrade, string? reason)
    {
        session.Demand(Permission.ManageTests);
        if (finalGrade < 1 || finalGrade > 5) throw new BusinessRuleException("Baho 1 dan 5 gacha.");
        using var db = _factory.Create();
        var r = db.TestResults.Include(x => x.Student).FirstOrDefault(x => x.Id == resultId) ?? throw new BusinessRuleException("Natija topilmadi.");
        if (finalGrade != r.AutoGrade && string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException($"{r.Student?.FullName}: yakuniy baho ({finalGrade}) avtomatik bahodan ({r.AutoGrade}) farq qiladi — sababini kiriting.");
        r.FinalGrade = finalGrade;
        r.IsConfirmed = true;
        r.ConfirmedByUserId = session.UserId;
        r.ConfirmedUtc = _utcNow();
        db.Audit(session, "Test bahosi tasdiqlandi", nameof(TestResult), r.Id, $"{r.Student?.FullName}: avtomatik {r.AutoGrade} → yakuniy {finalGrade}", reason);
        db.SaveChanges();
    }

    public void Unconfirm(UserSession session, int resultId, string reason)
    {
        session.Demand(Permission.ManageTests);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Sababini kiriting.");
        using var db = _factory.Create();
        var r = db.TestResults.Find(resultId) ?? throw new BusinessRuleException("Natija topilmadi.");
        r.IsConfirmed = false; r.FinalGrade = null; r.ConfirmedByUserId = null; r.ConfirmedUtc = null;
        db.Audit(session, "Test bahosi tasdig'i bekor qilindi", nameof(TestResult), r.Id, null, reason);
        db.SaveChanges();
    }

    public void DeleteResult(UserSession session, int resultId, string reason)
    {
        session.Demand(Permission.ManageTests);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("Sababini kiriting.");
        using var db = _factory.Create();
        var r = db.TestResults.Include(x => x.Student).FirstOrDefault(x => x.Id == resultId) ?? throw new BusinessRuleException("Natija topilmadi.");
        if (r.IsConfirmed && session.Role != UserRole.Administrator)
            throw new AccessDeniedException("Tasdiqlangan natijani faqat administrator o'chira oladi.");
        db.TestResults.Remove(r);
        db.Audit(session, "Test natijasi o'chirildi", nameof(TestResult), resultId, $"{r.Student?.FullName}: {r.Score}/{r.MaxScore}", reason);
        db.SaveChanges();
    }

    // ---------- Import ----------

    public static readonly ImportField[] ImportFields =
    {
        new("student", "O'quvchi (raqami yoki F.I.Sh.)", true, new[] { "o'quvchi", "f.i.sh.", "fish", "familiya ism", "o'quvchi raqami", "raqami", "student", "фио" }),
        new("variant", "Variant", false, new[] { "variant", "вариант" }),
        new("answers", "Javoblar", true, new[] { "javoblar", "javob", "answers", "ответы" }),
    };

    public List<ImportResultRow> ValidateImport(int assessmentId, int groupId, TabularData data, IReadOnlyDictionary<string, int> map)
    {
        var rows = new List<ImportResultRow>();
        foreach (var f in ImportFields.Where(f => f.Required))
            if (!map.TryGetValue(f.Key, out var c) || c < 0)
                return new List<ImportResultRow> { new(0, null, 1, "", $"Majburiy ustun tanlanmagan: \"{f.Label}\".") };
        var a = Get(assessmentId);
        using var db = _factory.Create();
        var students = db.Students.AsNoTracking().Where(s => s.GroupId == groupId).ToList();
        var seen = new HashSet<int>();
        for (int i = 0; i < data.Rows.Count; i++)
        {
            string Get2(string k) => map.TryGetValue(k, out var c) && c >= 0 ? data.Cell(i, c) : "";
            var who = Get2("student");
            var key = StudentImport.NameKey(who, "", "");
            var matches = students.Where(s => string.Equals(s.StudentNumber, who.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                matches = students.Where(s =>
                    ColumnMapper.Normalize(s.FullName) == ColumnMapper.Normalize(who) ||
                    ColumnMapper.Normalize(s.LastName + s.FirstName) == ColumnMapper.Normalize(who)).ToList();
            string? error = null;
            Student? st = null;
            if (matches.Count == 0) error = $"O'quvchi topilmadi: \"{who}\" (guruhda raqam yoki F.I.Sh. bo'yicha).";
            else if (matches.Count > 1) error = $"\"{who}\" — guruhda bir nechta o'quvchi mos keldi. O'quvchi raqamidan foydalaning.";
            else st = matches[0];
            if (st is not null && !seen.Add(st.Id)) error = $"{st.FullName} faylda takrorlangan.";
            var vText = Get2("variant");
            int variant = 1;
            if (!string.IsNullOrWhiteSpace(vText) && (!int.TryParse(vText.Trim(), out variant) || variant < 1 || variant > a.VariantCount))
                error ??= $"Variant noto'g'ri: \"{vText}\" (1–{a.VariantCount}).";
            var answers = Get2("answers");
            if (error is null)
            {
                var count = a.Questions.Count(q => q.IsApproved);
                var maxOpts = a.Questions.Where(q => q.IsApproved).Select(q => q.Options.Count).DefaultIfEmpty(4).Max();
                var (_, perr) = AnswerSheet.Parse(answers, count, maxOpts);
                error = perr;
            }
            rows.Add(new ImportResultRow(i + 2, st, variant, answers, error));
        }
        return rows;
    }

    public int ImportResults(UserSession session, int assessmentId, DateOnly date, IEnumerable<ImportResultRow> rows, bool newAttempts)
    {
        session.Demand(Permission.ManageTests);
        var a = Get(assessmentId);
        var maxOpts = a.Questions.Where(q => q.IsApproved).Select(q => q.Options.Count).DefaultIfEmpty(4).Max();
        var count = a.Questions.Count(q => q.IsApproved);
        int n = 0;
        foreach (var row in rows.Where(r => r.Error is null && r.Student is not null))
        {
            var (sel, err) = AnswerSheet.Parse(row.Answers, count, maxOpts);
            if (err is not null) continue;
            RecordResult(session, assessmentId, row.Student!.Id, date, row.Variant, sel, ResultMethod.Import, null, newAttempts);
            n++;
        }
        return n;
    }

    // ---------- Hisobotlar ----------

    public ReportTable ResultsReport(UserSession session, int assessmentId, int groupId)
    {
        var rows = ListResults(session, assessmentId, groupId);
        var a = Get(assessmentId);
        using var db = _factory.Create();
        var group = db.Groups.AsNoTracking().First(g => g.Id == groupId);
        var t = new ReportTable { Title = $"Test natijalari: {a.Title}", Landscape = true };
        t.SubtitleLines.Add($"Fan: {a.Subject?.Name}. Guruh: {group.Name}.");
        t.SubtitleLines.Add($"Baholash mezoni: {new GradingScale(a.Grade5Min, a.Grade4Min, a.Grade3Min).Describe()}");
        t.Columns.AddRange(new[] { "№", "F.I.Sh.", "Sana", "Variant", "Javoblar", "Ball", "Foiz", "Avto baho", "Yakuniy baho", "Holat", "Usul" });
        t.Widths.AddRange(new[] { 0.5f, 4f, 1.3f, 0.8f, 3.5f, 1f, 1f, 1f, 1.2f, 1.6f, 1.6f });
        int i = 0;
        foreach (var r in rows)
        {
            var res = r.Result;
            t.Rows.Add(new[]
            {
                (++i).ToString(), r.Student.FullName, res?.Date.ToString(ReportService.DateFormat) ?? "", res?.Variant.ToString() ?? "",
                r.Answers, res is null ? "" : $"{res.Score}/{res.MaxScore}", res is null ? "" : $"{res.Percent:0.#}%",
                res?.AutoGrade.ToString() ?? "", res?.FinalGrade?.ToString() ?? "",
                res is null ? "Topshirmagan" : res.IsConfirmed ? "Tasdiqlangan" : "Tasdiqlanmagan", res?.Method.ToUz() ?? "",
            });
        }
        var done = rows.Where(r => r.Result is not null).Select(r => r.Result!).ToList();
        if (done.Count > 0)
        {
            t.FooterLines.Add($"Topshirdi: {done.Count} / {rows.Count}. O'rtacha natija: {done.Average(r => r.Percent):0.#}%. " +
                $"Baholar (yakuniy, bo'lmasa avtomatik): " + string.Join(", ", Enumerable.Range(2, 4).Reverse().Select(g => $"\"{g}\" — {done.Count(r => (r.FinalGrade ?? r.AutoGrade) == g)}")) + ".");
            var unconfirmed = done.Count(r => !r.IsConfirmed);
            if (unconfirmed > 0) t.FooterLines.Add($"Diqqat: {unconfirmed} ta natija o'qituvchi tomonidan hali tasdiqlanmagan — ular yakuniy baho emas.");
        }
        t.FooterLines.Add($"Tayyorlandi: {DateTime.Now:dd.MM.yyyy HH:mm}, {session.FullName}");
        return t;
    }

    /// <summary>Savollar tahlili: har bir savolga to'g'ri javob bergan o'quvchilar ulushi.</summary>
    public ReportTable ItemAnalysis(UserSession session, int assessmentId, int groupId)
    {
        using var db = _factory.Create();
        DemandGroupForView(db, session, groupId);
        var a = Get(assessmentId);
        var resultIds = db.TestResults.AsNoTracking().Where(r => r.AssessmentId == assessmentId && r.GroupId == groupId).ToList()
            .GroupBy(r => r.StudentId).Select(g => g.OrderByDescending(r => r.AttemptNo).First().Id).ToList();
        var answers = db.TestAnswers.AsNoTracking().Where(x => resultIds.Contains(x.TestResultId)).ToList();
        var t = new ReportTable { Title = $"Savollar tahlili: {a.Title}" };
        t.SubtitleLines.Add($"Natijalar soni: {resultIds.Count}");
        t.Columns.AddRange(new[] { "№", "Savol", "To'g'ri", "Javobsiz", "To'g'ri javob %", "Izoh" });
        t.Widths.AddRange(new[] { 0.5f, 6f, 1f, 1f, 1.4f, 2.5f });
        foreach (var q in a.Questions.Where(q => q.IsApproved))
        {
            var qa = answers.Where(x => x.QuestionId == q.Id).ToList();
            var ok = qa.Count(x => x.IsCorrect);
            var pct = qa.Count == 0 ? (double?)null : Math.Round(100.0 * ok / qa.Count, 1);
            var hint = pct is null ? "" : pct < 30 ? "Juda qiyin yoki noaniq — qayta ko'rib chiqing" : pct > 95 ? "Juda oson" : "";
            t.Rows.Add(new[] { q.OrderNo.ToString(), q.Text, ok.ToString(), qa.Count(x => x.SelectedOptionId is null).ToString(), pct is null ? "—" : $"{pct:0.#}%", hint });
        }
        return t;
    }

    private static string Short(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
