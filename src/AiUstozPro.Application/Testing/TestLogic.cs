using System.Text;
using System.Text.Json;
using AiUstozPro.Application.Ai;

namespace AiUstozPro.Application.Testing;

/// <summary>Baholash mezoni (foizlar bo'yicha 5 ballik tizim). Sozlanadi.</summary>
public sealed record GradingScale(int Grade5Min, int Grade4Min, int Grade3Min)
{
    public static readonly GradingScale Default = new(86, 71, 56);

    public string? Validate()
    {
        if (Grade5Min > 100 || Grade3Min < 1) return "Foizlar 1 dan 100 gacha bo'lsin.";
        if (!(Grade5Min > Grade4Min && Grade4Min > Grade3Min)) return "Mezon noto'g'ri: \"5\" > \"4\" > \"3\" foizlari kamayib borishi kerak.";
        return null;
    }

    public int Grade(double percent) => percent >= Grade5Min ? 5 : percent >= Grade4Min ? 4 : percent >= Grade3Min ? 3 : 2;

    public string Describe() => $"5: {Grade5Min}–100%, 4: {Grade4Min}–{Grade5Min - 1}%, 3: {Grade3Min}–{Grade4Min - 1}%, 2: 0–{Grade3Min - 1}%";
}

/// <summary>Savol (variant tuzish va baholash uchun soddalashtirilgan ko'rinish).</summary>
public sealed record QuestionKey(int QuestionId, int Points, IReadOnlyList<int> OptionIds, int CorrectOptionId);

public sealed record VariantQuestion(int QuestionId, IReadOnlyList<int> OptionIds);

public sealed record TestVariant(int Number, IReadOnlyList<VariantQuestion> Questions)
{
    /// <summary>Variant bo'yicha to'g'ri javoblar harflari, masalan "BADC".</summary>
    public string AnswerKey(IReadOnlyDictionary<int, QuestionKey> keys)
        => new(Questions.Select(q => AnswerSheet.Letter(q.OptionIds.ToList().IndexOf(keys[q.QuestionId].CorrectOptionId))).ToArray());
}

/// <summary>
/// Variantlarni deterministik aralashtirish: bir xil test va variant raqami uchun har doim bir xil tartib
/// (chop etilgan varaq va kiritilgan javoblar mos kelishi uchun). 1-variant — asl tartib.
/// </summary>
public static class VariantBuilder
{
    public static TestVariant Build(int assessmentId, int variant, IReadOnlyList<QuestionKey> questions, bool shuffleQuestions, bool shuffleOptions)
    {
        if (variant < 1) throw new ArgumentOutOfRangeException(nameof(variant));
        var rng = new XorShift((uint)(assessmentId * 7919 + variant * 104729 + 17));
        var order = questions.ToList();
        if (variant > 1 && shuffleQuestions) Shuffle(order, rng);
        var list = order.Select(q =>
        {
            var opts = q.OptionIds.ToList();
            if (variant > 1 && shuffleOptions) Shuffle(opts, rng);
            return new VariantQuestion(q.QuestionId, opts);
        }).ToList();
        return new TestVariant(variant, list);
    }

    private static void Shuffle<T>(List<T> list, XorShift rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.Next() % (uint)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>.NET versiyasidan qat'i nazar bir xil natija beradigan oddiy PRNG.</summary>
    private sealed class XorShift
    {
        private uint _s;
        public XorShift(uint seed) => _s = seed == 0 ? 2463534242u : seed;
        public uint Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s;
        }
    }
}

/// <summary>Javoblar varaqasi: "ABCD-A" ko'rinishidagi satr.</summary>
public static class AnswerSheet
{
    public const string Letters = "ABCDEFGH";

    public static char Letter(int index) => index >= 0 && index < Letters.Length ? Letters[index] : '?';

    /// <summary>
    /// Harflarni variant indekslariga aylantiradi. Bo'sh javob: '-', '_', '.', '0'.
    /// Kirill А, В, С, Д, Е harflari va bo'shliq/vergul ajratgichlari ham qabul qilinadi.
    /// </summary>
    public static (List<int?> Selections, string? Error) Parse(string? text, int questionCount, int maxOptions = 8)
    {
        var cleaned = new StringBuilder();
        foreach (var ch in (text ?? "").ToUpperInvariant())
        {
            if (ch is ' ' or ',' or ';' or '\t') continue;
            cleaned.Append(ch switch { 'А' => 'A', 'В' => 'B', 'С' => 'C', 'Д' => 'D', 'Е' => 'E', 'Б' => 'B', _ => ch });
        }
        var s = cleaned.ToString();
        if (s.Length != questionCount)
            return (new List<int?>(), $"Javoblar soni {s.Length} ta, savollar esa {questionCount} ta.");
        var list = new List<int?>();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '-' or '_' or '.' or '0') { list.Add(null); continue; }
            var idx = Letters.IndexOf(c);
            if (idx < 0 || idx >= maxOptions) return (new List<int?>(), $"{i + 1}-savol: \"{c}\" — noto'g'ri belgi (A–{Letter(maxOptions - 1)} yoki '-').");
            list.Add(idx);
        }
        return (list, null);
    }

    public static string Format(IEnumerable<int?> selections) => new(selections.Select(s => s is int i ? Letter(i) : '-').ToArray());
}

public sealed record ScoredAnswer(int QuestionId, int? SelectedOptionId, bool IsCorrect, int Points);

public sealed record ScoreResult(IReadOnlyList<ScoredAnswer> Answers, int Score, int MaxScore)
{
    public double Percent => MaxScore == 0 ? 0 : Math.Round(100.0 * Score / MaxScore, 1);
}

public static class Scorer
{
    /// <param name="selections">Variantdagi har bir savol uchun tanlangan variant indeksi (null — javobsiz).</param>
    public static ScoreResult Score(TestVariant variant, IReadOnlyDictionary<int, QuestionKey> keys, IReadOnlyList<int?> selections)
    {
        if (selections.Count != variant.Questions.Count) throw new ArgumentException("Javoblar soni savollar soniga teng emas.");
        var answers = new List<ScoredAnswer>();
        int score = 0, max = 0;
        for (int i = 0; i < variant.Questions.Count; i++)
        {
            var vq = variant.Questions[i];
            var key = keys[vq.QuestionId];
            max += key.Points;
            int? optionId = selections[i] is int idx && idx < vq.OptionIds.Count ? vq.OptionIds[idx] : null;
            var ok = optionId == key.CorrectOptionId;
            var pts = ok ? key.Points : 0;
            score += pts;
            answers.Add(new ScoredAnswer(vq.QuestionId, optionId, ok, pts));
        }
        return new ScoreResult(answers, score, max);
    }
}

public sealed record ParsedQuestion(string Text, IReadOnlyList<string> Options, int CorrectIndex);

/// <summary>AI qaytargan JSON savollarni o'qiydi. Har bir savol keyin o'qituvchi tomonidan tasdiqlanadi.</summary>
public static class AiQuestionParser
{
    public static string BuildPrompt(AiTaskContext c)
        => (string.IsNullOrWhiteSpace(c.Subject) ? "" : $"Fan: {c.Subject}\n") +
           (string.IsNullOrWhiteSpace(c.Course) ? "" : $"Sinf/kurs: {c.Course}\n") +
           $"Mavzu: {c.Topic}\n" +
           (string.IsNullOrWhiteSpace(c.ExpectedOutcome) ? "" : $"Kutilayotgan natija: {c.ExpectedOutcome}\n") +
           $"\nShu mavzu bo'yicha {c.Count} ta test savoli tuz. Murakkablik: {c.Level}. Har bir savolda 4 ta variant, faqat bittasi to'g'ri. " +
           "Javobni FAQAT quyidagi JSON massiv ko'rinishida ber, boshqa hech qanday matn yozma:\n" +
           "[{\"savol\": \"...\", \"variantlar\": [\"...\", \"...\", \"...\", \"...\"], \"togri\": 0}]\n" +
           "\"togri\" — to'g'ri variantning 0 dan boshlanadigan indeksi." +
           (string.IsNullOrWhiteSpace(c.Extra) ? "" : $"\nQo'shimcha talab: {c.Extra.Trim()}");

    public static (List<ParsedQuestion> Questions, List<string> Errors) Parse(string text)
    {
        var questions = new List<ParsedQuestion>();
        var errors = new List<string>();
        var start = (text ?? "").IndexOf('[');
        var end = (text ?? "").LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            errors.Add("AI javobida JSON massiv topilmadi.");
            return (questions, errors);
        }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text![start..(end + 1)]); }
        catch (JsonException ex)
        {
            errors.Add("AI javobi JSON formatida emas: " + ex.Message);
            return (questions, errors);
        }
        using (doc)
        {
            int n = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                n++;
                try
                {
                    var q = Str(el, "savol", "question");
                    var opts = new List<string>();
                    if (TryProp(el, out var arr, "variantlar", "options") && arr.ValueKind == JsonValueKind.Array)
                        foreach (var o in arr.EnumerateArray()) opts.Add(o.ValueKind == JsonValueKind.String ? o.GetString()!.Trim() : o.ToString());
                    int correct = TryProp(el, out var c, "togri", "to'g'ri", "correct") && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
                    if (string.IsNullOrWhiteSpace(q)) { errors.Add($"{n}-savol: matn bo'sh."); continue; }
                    opts = opts.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
                    if (opts.Count < 2 || opts.Count > 8) { errors.Add($"{n}-savol: variantlar soni noto'g'ri ({opts.Count})."); continue; }
                    if (correct < 0 || correct >= opts.Count) { errors.Add($"{n}-savol: to'g'ri javob ko'rsatilmagan."); continue; }
                    if (opts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != opts.Count) { errors.Add($"{n}-savol: bir xil variantlar bor."); continue; }
                    questions.Add(new ParsedQuestion(q.Trim(), opts, correct));
                }
                catch (InvalidOperationException) { errors.Add($"{n}-savol: format noto'g'ri."); }
            }
        }
        return (questions, errors);
    }

    private static string Str(JsonElement el, params string[] names)
        => TryProp(el, out var v, names) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool TryProp(JsonElement el, out JsonElement value, params string[] names)
    {
        foreach (var n in names)
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out value)) return true;
        value = default;
        return false;
    }
}
