using System.Collections.ObjectModel;
using AiUstozPro.App.Dialogs;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Ai;
using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Application.Testing;
using AiUstozPro.Domain;
using AiUstozPro.Infrastructure.Reports;
using AiUstozPro.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiUstozPro.App.ViewModels;

public sealed class QuestionRow
{
    public required TestQuestion Q { get; init; }
    public int No => Q.OrderNo;
    public string Text => Q.Text;
    public int Points => Q.Points;
    public string Options => string.Join("   ", Q.Options.Select((o, i) => $"{AnswerSheet.Letter(i)}) {o.Text}{(o.IsCorrect ? " ✔" : "")}"));
    public string Source => Q.Source == "ai" ? "AI" : "Qo'lda";
    public string Approved => Q.IsApproved ? "Ha" : "Tekshirilmagan";
    public bool NeedsReview => !Q.IsApproved;
}

public sealed partial class OptionEdit : ObservableObject
{
    public required string Letter { get; init; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isCorrect;
}

public sealed partial class ResultEdit : ObservableObject
{
    public required Student Student { get; init; }
    public TestResult? Result { get; set; }
    public string Name => Student.FullName + (Student.IsActive ? "" : " (arxiv)");
    [ObservableProperty] private string _variant = "1";
    [ObservableProperty] private string _answers = "";
    [ObservableProperty] private int? _finalGrade;
    public string OriginalAnswers { get; set; } = "";
    public string OriginalVariant { get; set; } = "";
    public bool IsDirty => Answers.Trim() != OriginalAnswers || Variant.Trim() != OriginalVariant;
    public string Score => Result is null ? "" : $"{Result.Score}/{Result.MaxScore}";
    public string Percent => Result is null ? "" : $"{Result.Percent:0.#}%";
    public string AutoGrade => Result?.AutoGrade.ToString() ?? "";
    public string State => Result is null ? "Topshirmagan" : Result.IsConfirmed ? "Tasdiqlangan" : "Tasdiqlanmagan";
    public string Info => Result is null ? "" : $"{Result.Date:dd.MM.yyyy}, {Result.Method.ToUz()}" + (Result.AttemptNo > 1 ? $", {Result.AttemptNo}-urinish" : "")
        + (Result.DurationSeconds is int d ? $", {d / 60}:{d % 60:00}" : "");
    public bool IsConfirmed => Result?.IsConfirmed == true;
}

public sealed partial class TestsViewModel : PageViewModel
{
    public override string Title => "Test va baholash";
    public bool CanEdit => Can(Permission.ManageTests);

    public ObservableCollection<Assessment> Tests { get; } = new();
    public ObservableCollection<Subject> Subjects { get; } = new();
    public ObservableCollection<QuestionRow> Questions { get; } = new();
    public ObservableCollection<OptionEdit> OptionEdits { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<ResultEdit> Results { get; } = new();
    public List<int?> Grades { get; } = new() { null, 5, 4, 3, 2 };
    public List<string> Levels { get; } = new() { "oson", "o'rta", "murakkab" };

    [ObservableProperty] private Assessment? _selectedTest;
    [ObservableProperty] private bool _hasTest;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summary = "";

    // Test sozlamalari
    [ObservableProperty] private int _testId;
    [ObservableProperty] private string _testTitle = "";
    [ObservableProperty] private Subject? _testSubject;
    [ObservableProperty] private string _timeLimit = "20";
    [ObservableProperty] private string _variantCount = "2";
    [ObservableProperty] private bool _shuffleQuestions = true;
    [ObservableProperty] private bool _shuffleOptions = true;
    [ObservableProperty] private string _g5 = "86";
    [ObservableProperty] private string _g4 = "71";
    [ObservableProperty] private string _g3 = "56";

    // Savol tahrirlash
    [ObservableProperty] private QuestionRow? _selectedQuestion;
    [ObservableProperty] private int _questionId;
    [ObservableProperty] private string _questionText = "";
    [ObservableProperty] private string _questionPoints = "1";
    [ObservableProperty] private string _aiTopic = "";
    [ObservableProperty] private string _aiCount = "10";
    [ObservableProperty] private string _aiLevel = "o'rta";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private bool _aiEnabled;

    // Natijalar
    [ObservableProperty] private Group? _resultGroup;
    [ObservableProperty] private DateTime? _resultDate = DateTime.Today;
    [ObservableProperty] private ResultEdit? _selectedResult;
    [ObservableProperty] private bool _newAttempt;
    [ObservableProperty] private string _resultsSummary = "";

    public TestsViewModel()
    {
        foreach (var l in AnswerSheet.Letters.Take(6)) OptionEdits.Add(new OptionEdit { Letter = l.ToString() });
    }

    public override void OnActivated()
    {
        Ui.Run(() =>
        {
            Subjects.Clear();
            foreach (var s in S.Academic.ListSubjects()) Subjects.Add(s);
            Groups.Clear();
            foreach (var g in S.Academic.ListGroups(Session)) Groups.Add(g);
            ResultGroup ??= Groups.FirstOrDefault();
            AiEnabled = S.Ai.IsEnabled;
            LoadTests(SelectedTest?.Id);
        });
    }

    private void LoadTests(int? selectId)
    {
        Tests.Clear();
        foreach (var t in S.Tests.ListAssessments(Session)) Tests.Add(t);
        var target = Tests.FirstOrDefault(t => t.Id == selectId);
        if (target is null) { SelectedTest = null; NewTest(); }
        else if (!ReferenceEquals(target, SelectedTest)) SelectedTest = target;
        else LoadTest();
    }

    partial void OnSelectedTestChanged(Assessment? value) => LoadTest();

    private void LoadTest()
    {
        Questions.Clear();
        HasTest = SelectedTest is not null;
        if (SelectedTest is null) return;
        Ui.Run(() =>
        {
            var a = S.Tests.Get(SelectedTest.Id);
            TestId = a.Id; TestTitle = a.Title; TestSubject = Subjects.FirstOrDefault(s => s.Id == a.SubjectId);
            TimeLimit = a.TimeLimitMinutes.ToString(); VariantCount = a.VariantCount.ToString();
            ShuffleQuestions = a.ShuffleQuestions; ShuffleOptions = a.ShuffleOptions;
            G5 = a.Grade5Min.ToString(); G4 = a.Grade4Min.ToString(); G3 = a.Grade3Min.ToString();
            foreach (var q in a.Questions) Questions.Add(new QuestionRow { Q = q });
            var unapproved = a.Questions.Count(q => !q.IsApproved);
            StatusText = $"Holat: {a.Status.ToUz()}. Savollar: {a.Questions.Count} ({a.Questions.Sum(q => q.Points)} ball)"
                + (unapproved > 0 ? $", tekshirilmagan AI savollari: {unapproved}" : "") + ".";
            Summary = new GradingScale(a.Grade5Min, a.Grade4Min, a.Grade3Min).Describe();
            if (string.IsNullOrWhiteSpace(AiTopic)) AiTopic = a.Title;
            NewQuestion();
            LoadResults();
        });
    }

    // ---------- Test ----------

    [RelayCommand]
    private void NewTest()
    {
        SelectedTest = null;
        TestId = 0; TestTitle = ""; TestSubject = Subjects.FirstOrDefault(); TimeLimit = "20"; VariantCount = "2";
        ShuffleQuestions = true; ShuffleOptions = true; G5 = "86"; G4 = "71"; G3 = "56";
        StatusText = "Yangi test. Sozlamalarni kiritib \"Saqlash\" ni bosing, so'ng savollar qo'shing.";
        Summary = GradingScale.Default.Describe();
        Questions.Clear(); Results.Clear();
        HasTest = false;
    }

    [RelayCommand]
    private void SaveTest()
    {
        if (TestSubject is null) { Ui.Warn("Fanni tanlang (avval \"Fanlar\" bo'limida qo'shing)."); return; }
        if (!int.TryParse(TimeLimit, out var tl) || !int.TryParse(VariantCount, out var vc) || !int.TryParse(G5, out var g5)
            || !int.TryParse(G4, out var g4) || !int.TryParse(G3, out var g3)) { Ui.Warn("Raqamli maydonlarni tekshiring."); return; }
        Assessment? saved = null;
        if (Ui.Run(() => saved = S.Tests.Save(Session, new Assessment
            {
                Id = TestId, Title = TestTitle, SubjectId = TestSubject.Id, TimeLimitMinutes = tl, VariantCount = vc,
                ShuffleQuestions = ShuffleQuestions, ShuffleOptions = ShuffleOptions, Grade5Min = g5, Grade4Min = g4, Grade3Min = g3,
            }), "Test saqlandi"))
            LoadTests(saved!.Id);
    }

    [RelayCommand]
    private void DuplicateTest()
    {
        if (SelectedTest is not { } t) return;
        Assessment? copy = null;
        if (Ui.Run(() => copy = S.Tests.Duplicate(Session, t.Id), "Nusxa yaratildi — endi uni tahrirlashingiz mumkin")) LoadTests(copy!.Id);
    }

    [RelayCommand]
    private void MarkReady()
    {
        if (SelectedTest is not { } t) return;
        if (!Ui.Confirm("Test \"Tayyor\" deb belgilansinmi? Shundan keyin natijalar kiritish mumkin bo'ladi. Natija kiritilgach savollarni o'zgartirib bo'lmaydi.")) return;
        if (Ui.Run(() => S.Tests.SetStatus(Session, t.Id, AssessmentStatus.Ready), "Test tayyor")) LoadTests(t.Id);
    }

    [RelayCommand]
    private void Archive()
    {
        if (SelectedTest is not { } t) return;
        if (!Ui.Confirm($"\"{t.Title}\" arxivlansinmi? Natijalar saqlanadi.")) return;
        if (Ui.Run(() => S.Tests.SetStatus(Session, t.Id, AssessmentStatus.Archived), "Test arxivlandi")) LoadTests(null);
    }

    // ---------- Savollar ----------

    partial void OnSelectedQuestionChanged(QuestionRow? value)
    {
        if (value is null) return;
        QuestionId = value.Q.Id;
        QuestionText = value.Q.Text;
        QuestionPoints = value.Q.Points.ToString();
        for (int i = 0; i < OptionEdits.Count; i++)
        {
            var o = i < value.Q.Options.Count ? value.Q.Options[i] : null;
            OptionEdits[i].Text = o?.Text ?? "";
            OptionEdits[i].IsCorrect = o?.IsCorrect == true;
        }
    }

    [RelayCommand]
    private void NewQuestion()
    {
        SelectedQuestion = null;
        QuestionId = 0; QuestionText = ""; QuestionPoints = "1";
        foreach (var o in OptionEdits) { o.Text = ""; o.IsCorrect = false; }
        OptionEdits[0].IsCorrect = true;
    }

    [RelayCommand]
    private void SaveQuestion()
    {
        if (SelectedTest is not { } t) { Ui.Warn("Avval testni saqlang."); return; }
        if (!int.TryParse(QuestionPoints, out var pts)) { Ui.Warn("Ball butun son bo'lsin."); return; }
        var opts = OptionEdits.Where(o => !string.IsNullOrWhiteSpace(o.Text)).Select(o => new OptionInput(o.Text, o.IsCorrect)).ToList();
        var wasAi = SelectedQuestion?.Q.Source == "ai";
        if (Ui.Run(() =>
            {
                var q = S.Tests.SaveQuestion(Session, t.Id, new QuestionInput(QuestionId, QuestionText, pts, opts), wasAi ? "ai" : "manual");
                if (wasAi && !q.IsApproved && Ui.Confirm("Bu savol AI tomonidan yaratilgan. Tekshirdingiz va to'g'ri deb tasdiqlaysizmi?"))
                    S.Tests.ApproveQuestion(Session, q.Id);
            }, "Savol saqlandi"))
            LoadTest();
    }

    [RelayCommand]
    private void ApproveQuestion()
    {
        if (SelectedQuestion is not { } q) return;
        if (!Ui.Confirm($"Savolni tekshirdingizmi?\n\n{q.Text}\n{q.Options}\n\nTo'g'ri deb tasdiqlaysizmi?")) return;
        if (Ui.Run(() => S.Tests.ApproveQuestion(Session, q.Q.Id), "Savol tasdiqlandi")) LoadTest();
    }

    [RelayCommand]
    private void DeleteQuestion()
    {
        if (SelectedQuestion is not { } q) return;
        if (!Ui.Confirm($"{q.No}-savol o'chirilsinmi?", dangerous: true)) return;
        if (Ui.Run(() => S.Tests.DeleteQuestion(Session, q.Q.Id), "Savol o'chirildi")) LoadTest();
    }

    [RelayCommand] private void MoveUp() { if (SelectedQuestion is { } q && Ui.Run(() => S.Tests.MoveQuestion(Session, q.Q.Id, -1))) LoadTest(); }
    [RelayCommand] private void MoveDown() { if (SelectedQuestion is { } q && Ui.Run(() => S.Tests.MoveQuestion(Session, q.Q.Id, +1))) LoadTest(); }

    [RelayCommand]
    private async Task GenerateWithAi()
    {
        if (SelectedTest is not { } t) { Ui.Warn("Avval testni saqlang."); return; }
        if (!S.Ai.IsEnabled) { Ui.Warn("AI yordamchi sozlanmagan (Sozlamalar → AI xizmati)."); return; }
        if (string.IsNullOrWhiteSpace(AiTopic)) { Ui.Warn("Mavzuni kiriting."); return; }
        var ctx = new AiTaskContext
        {
            Subject = TestSubject?.Name ?? "", Topic = AiTopic.Trim(), Level = AiLevel,
            Count = int.TryParse(AiCount, out var n) && n > 0 && n <= 30 ? n : 10,
        };
        AiBusy = true;
        try
        {
            var r = await S.Ai.AskAsync(Session, null, "testjson", $"Test savollari: {ctx.Topic}", AiQuestionParser.BuildPrompt(ctx), CancellationToken.None);
            var (parsed, errors) = AiQuestionParser.Parse(r.Answer.Content);
            int added = 0;
            if (parsed.Count > 0 && !Ui.Run(() => added = S.Tests.AddAiQuestions(Session, t.Id, parsed))) return;
            LoadTest();
            Ui.Info($"AI {added} ta savol taklif qildi. Ular \"Tekshirilmagan\" holatida — har birini o'qib, kerak bo'lsa tahrirlab, \"Tasdiqlash\" tugmasini bosing. Tasdiqlanmagan savollar testga kirmaydi."
                + (errors.Count > 0 ? $"\n\nQabul qilinmagan: {errors.Count} ta ({string.Join("; ", errors.Take(3))})" : ""));
        }
        catch (AiException ex) { Ui.Warn(ex.Message); }
        catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException) { Ui.Warn(ex.Message); }
        catch (Exception ex) { Log.Error(ex, "AI test yaratish"); Ui.Error(ex.Message); }
        finally { AiBusy = false; }
    }

    [RelayCommand]
    private void Print()
    {
        if (SelectedTest is not { } t) return;
        var path = Ui.SaveFile($"{Sanitize(t.Title)} — variantlar.docx", "Word (*.docx)|*.docx|PDF (*.pdf)|*.pdf");
        if (path is null) return;
        var fmt = ReportExporter.FormatFromPath(path);
        Ui.Run(() =>
        {
            var a = S.Tests.Get(t.Id);
            var text = string.Join("\n\n", Enumerable.Range(1, a.VariantCount).Select(v => S.Tests.PrintableText(t.Id, v)));
            ReportExporter.ExportText(a.Title, new[] { $"{a.VariantCount} ta variant" }, text, fmt, path);
            var keyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileNameWithoutExtension(path) + " — javoblar kaliti" + ReportExporter.Extension(fmt));
            ReportExporter.Export(S.Tests.AnswerKeys(t.Id), fmt, keyPath);
            Ui.Info($"Saqlandi:\n• {path}\n• {keyPath}\n\nJavoblar kalitini o'quvchilarga bermang.");
            Ui.ShowInFolder(path);
        });
    }

    // ---------- Natijalar ----------

    partial void OnResultGroupChanged(Group? value) => LoadResults();

    private void LoadResults()
    {
        Results.Clear();
        if (SelectedTest is null || ResultGroup is null) { ResultsSummary = ""; return; }
        Ui.Run(() =>
        {
            foreach (var r in S.Tests.ListResults(Session, SelectedTest.Id, ResultGroup.Id))
            {
                var v = r.Result?.Variant.ToString() ?? "";
                Results.Add(new ResultEdit
                {
                    Student = r.Student, Result = r.Result, Variant = v, OriginalVariant = v,
                    Answers = r.Answers, OriginalAnswers = r.Answers, FinalGrade = r.Result?.FinalGrade ?? r.Result?.AutoGrade,
                });
            }
            var done = Results.Where(x => x.Result is not null).ToList();
            ResultsSummary = done.Count == 0 ? "Natijalar yo'q. Javoblarni kiriting (masalan ABCDA, javobsiz — \"-\"), import qiling yoki o'quvchini kompyuterda topshirtiring."
                : $"Topshirdi: {done.Count} / {Results.Count}, o'rtacha {done.Average(x => x.Result!.Percent):0.#}%. Tasdiqlanmagan: {done.Count(x => !x.IsConfirmed)}.";
        });
    }

    [RelayCommand]
    private void SaveAnswers()
    {
        if (SelectedTest is not { } t || ResultDate is not { } d) return;
        var dirty = Results.Where(r => r.IsDirty && r.Answers.Trim().Length > 0).ToList();
        if (dirty.Count == 0) { Ui.Info("O'zgartirilgan javoblar yo'q."); return; }
        var a = S.Tests.Get(t.Id);
        var count = a.Questions.Count(q => q.IsApproved);
        var maxOpts = a.Questions.Where(q => q.IsApproved).Select(q => q.Options.Count).DefaultIfEmpty(4).Max();
        var errors = new List<string>();
        int saved = 0;
        foreach (var r in dirty)
        {
            if (!int.TryParse(string.IsNullOrWhiteSpace(r.Variant) ? "1" : r.Variant, out var v)) { errors.Add($"{r.Name}: variant noto'g'ri"); continue; }
            var (sel, err) = AnswerSheet.Parse(r.Answers, count, maxOpts);
            if (err is not null) { errors.Add($"{r.Name}: {err}"); continue; }
            try
            {
                S.Tests.RecordResult(Session, t.Id, r.Student.Id, DateOnly.FromDateTime(d), v, sel, ResultMethod.ManualEntry, null, NewAttempt);
                saved++;
            }
            catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException) { errors.Add($"{r.Name}: {ex.Message}"); }
        }
        LoadResults();
        if (errors.Count > 0) Ui.Warn($"{saved} ta saqlandi. Xatolar:\n" + string.Join("\n", errors.Take(15)));
        else Toast.Show($"{saved} ta natija saqlandi va baholandi (tasdiqlanmagan)");
    }

    [RelayCommand]
    private void ConfirmGrades()
    {
        var targets = Results.Where(r => r.Result is not null && !r.IsConfirmed).ToList();
        if (targets.Count == 0) { Ui.Info("Tasdiqlanadigan natija yo'q."); return; }
        if (!Ui.Confirm($"{targets.Count} ta natijaning yakuniy bahosi tasdiqlansinmi? Avtomatik bahodan farq qilgan holatlar uchun sabab so'raladi.")) return;
        int n = 0;
        foreach (var r in targets)
        {
            var grade = r.FinalGrade ?? r.Result!.AutoGrade;
            string? reason = null;
            if (grade != r.Result!.AutoGrade)
            {
                reason = Ui.Ask("Bahoni o'zgartirish sababi", $"{r.Name}: avtomatik baho {r.Result.AutoGrade}, siz {grade} qo'ydingiz. Sababini kiriting:");
                if (reason is null) continue;
            }
            if (Ui.Run(() => S.Tests.Confirm(Session, r.Result.Id, grade, reason))) n++;
        }
        LoadResults();
        Toast.Show($"{n} ta baho tasdiqlandi");
    }

    [RelayCommand]
    private void Unconfirm()
    {
        if (SelectedResult?.Result is not { IsConfirmed: true } res) { Ui.Warn("Tasdiqlangan natijani tanlang."); return; }
        var reason = Ui.Ask("Tasdiqni bekor qilish", "Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Tests.Unconfirm(Session, res.Id, reason), "Tasdiq bekor qilindi")) LoadResults();
    }

    [RelayCommand]
    private void DeleteResult()
    {
        if (SelectedResult?.Result is not { } res) return;
        var reason = Ui.Ask("Natijani o'chirish", $"{SelectedResult.Name} natijasi o'chiriladi. Sababini kiriting:");
        if (reason is null) return;
        if (Ui.Run(() => S.Tests.DeleteResult(Session, res.Id, reason), "Natija o'chirildi")) LoadResults();
    }

    [RelayCommand]
    private void TakeOnComputer()
    {
        if (SelectedTest is not { } t || SelectedResult is not { } row || ResultDate is not { } d) { Ui.Warn("O'quvchini tanlang."); return; }
        try
        {
            var a = S.Tests.Get(t.Id);
            if (a.Status != AssessmentStatus.Ready) { Ui.Warn("Test \"Tayyor\" holatida emas."); return; }
            if (row.IsConfirmed && !NewAttempt) { Ui.Warn("Bu o'quvchi natijasi tasdiqlangan. Qayta topshirish uchun \"Qayta topshirish\" belgisini qo'ying."); return; }
            var index = Results.IndexOf(row);
            var variant = index % a.VariantCount + 1;
            var (v, _, _) = S.Tests.GetVariant(t.Id, variant);
            var byId = a.Questions.ToDictionary(q => q.Id);
            var opts = a.Questions.SelectMany(q => q.Options).ToDictionary(o => o.Id);
            var qs = v.Questions.Select(q => new TakingQuestion(byId[q.QuestionId].Text, q.OptionIds.Select(id => opts[id].Text).ToList(), byId[q.QuestionId].Points)).ToList();
            if (!Ui.Confirm($"{row.Name} uchun {variant}-variant ochiladi" + (a.TimeLimitMinutes > 0 ? $" ({a.TimeLimitMinutes} daqiqa)" : "") + ". O'quvchini kompyuterga taklif qiling. Boshlaymizmi?")) return;
            var win = new TestTakingWindow(a.Title, row.Name, variant, qs, a.TimeLimitMinutes) { Owner = Ui.Owner };
            win.ShowDialog();
            if (!win.Completed) return;
            Ui.Run(() => S.Tests.RecordResult(Session, t.Id, row.Student.Id, DateOnly.FromDateTime(d), variant, win.Selections, ResultMethod.Computer,
                win.DurationSeconds, NewAttempt, win.TimedOut ? "Vaqt tugadi — avtomatik topshirildi" : null), "Test qabul qilindi. Bahoni tasdiqlashni unutmang.");
            LoadResults();
        }
        catch (Exception ex) when (ex is BusinessRuleException or AccessDeniedException) { Ui.Warn(ex.Message); }
    }

    [RelayCommand]
    private void ImportResults()
    {
        if (SelectedTest is not { } t || ResultGroup is not { } g || ResultDate is not { } d) return;
        var dlg = new ImportDialog("Test natijalarini import qilish",
            "Ustunlar: O'quvchi (raqami yoki F.I.Sh.), Variant (ixtiyoriy, standart 1), Javoblar (masalan ABCDA, javobsiz — \"-\"). Masalan, Google Forms yoki boshqa tizim natijalarini Excel ga o'tkazib, shu yerga yuklang.",
            AssessmentService.ImportFields,
            (data, map) => S.Tests.ValidateImport(t.Id, g.Id, data, map).Select(r => new PreviewRow
            {
                RowNumber = r.RowNumber, IsValid = r.Error is null, Item = r, Messages = r.Error ?? "",
                Values = new[] { r.Student?.FullName ?? "", r.Variant.ToString(), r.Answers },
            }).ToList(),
            optionText: "Har bir satrni qayta topshirish (yangi urinish) sifatida qo'shish", optionDefault: NewAttempt) { Owner = Ui.Owner };
        if (dlg.ShowDialog() != true) return;
        int n = 0;
        if (Ui.Run(() => n = S.Tests.ImportResults(Session, t.Id, DateOnly.FromDateTime(d), dlg.ValidItems.Cast<ImportResultRow>(), dlg.OptionBox?.IsChecked == true)))
        {
            LoadResults();
            Toast.Show($"{n} ta natija import qilindi (tasdiqlanmagan)");
        }
    }

    [RelayCommand]
    private void ExportResults()
    {
        if (SelectedTest is not { } t || ResultGroup is not { } g) return;
        var path = Ui.SaveFile($"{Sanitize(t.Title)} — {g.Name} natijalar.xlsx", ReportExporter.SaveFilter);
        if (path is null) return;
        if (Ui.Run(() => ReportExporter.Export(S.Tests.ResultsReport(Session, t.Id, g.Id), ReportExporter.FormatFromPath(path), path), "Hisobot saqlandi"))
            Ui.OpenPath(path);
    }

    [RelayCommand]
    private void ExportAnalysis()
    {
        if (SelectedTest is not { } t || ResultGroup is not { } g) return;
        var path = Ui.SaveFile($"{Sanitize(t.Title)} — savollar tahlili.xlsx", ReportExporter.SaveFilter);
        if (path is null) return;
        if (Ui.Run(() => ReportExporter.Export(S.Tests.ItemAnalysis(Session, t.Id, g.Id), ReportExporter.FormatFromPath(path), path), "Tahlil saqlandi"))
            Ui.OpenPath(path);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 60 ? s[..60] : s;
    }
}
