using System.Text;

namespace AiUstozPro.Application.Ai;

/// <summary>AI topshirig'i uchun kontekst (KTR mavzusidan yoki qo'lda kiritiladi).</summary>
public sealed class AiTaskContext
{
    public string Subject { get; set; } = "";
    public string Course { get; set; } = "";
    public string Topic { get; set; } = "";
    public int Hours { get; set; } = 2;
    public string LessonType { get; set; } = "";
    public string ExpectedOutcome { get; set; } = "";
    /// <summary>Oson / o'rta / murakkab.</summary>
    public string Level { get; set; } = "o'rta";
    public int Count { get; set; } = 10;
    /// <summary>Qo'shimcha matn: kod, eslatma, tahlil uchun ma'lumot.</summary>
    public string Extra { get; set; } = "";
}

public sealed record AiTaskTemplate(string Key, string Title, string Hint, bool NeedsTopic, bool NeedsExtra, Func<AiTaskContext, string> Build);

public static class PromptTemplates
{
    public const string SystemPrompt =
        "Sen o'zbek tilida (lotin alifbosida) javob beradigan tajribali informatika o'qituvchisining yordamchisisan. " +
        "Javoblaring O'zbekiston akademik litseylari va maktablari o'quv dasturiga mos, aniq, tushunarli va amaliy bo'lsin. " +
        "Agar biror faktga ishonching komil bo'lmasa, buni ochiq ayt va o'qituvchidan tekshirishni so'ra — taxminni fakt sifatida berma. " +
        "Kod misollarini ``` bloklarida ber va qaysi tilda ekanini ko'rsat. " +
        "Sarlavhalar uchun '#', ro'yxatlar uchun '-' belgisidan foydalan. " +
        "O'quvchilarning shaxsiy ma'lumotlarini so'rama va o'ylab topma.";

    private static string Header(AiTaskContext c)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(c.Subject)) sb.AppendLine($"Fan: {c.Subject}");
        if (!string.IsNullOrWhiteSpace(c.Course)) sb.AppendLine($"Sinf/kurs: {c.Course}");
        if (!string.IsNullOrWhiteSpace(c.Topic)) sb.AppendLine($"Mavzu: {c.Topic}");
        if (c.Hours > 0 && !string.IsNullOrWhiteSpace(c.Topic)) sb.AppendLine($"Ajratilgan vaqt: {c.Hours} akademik soat (1 soat = 45 daqiqa)");
        if (!string.IsNullOrWhiteSpace(c.LessonType)) sb.AppendLine($"Mashg'ulot turi: {c.LessonType}");
        if (!string.IsNullOrWhiteSpace(c.ExpectedOutcome)) sb.AppendLine($"KTR bo'yicha kutilayotgan natija: {c.ExpectedOutcome}");
        return sb.ToString();
    }

    private static string WithExtra(AiTaskContext c, string label)
        => string.IsNullOrWhiteSpace(c.Extra) ? "" : $"\n{label}:\n{c.Extra.Trim()}\n";

    public static readonly IReadOnlyList<AiTaskTemplate> All = new AiTaskTemplate[]
    {
        new("explain", "Mavzuni tushuntirish", "Mavzuni sodda va batafsil tushuntiradi, misollar bilan.", true, false, c =>
            Header(c) + "\nShu mavzuni o'quvchilarga tushuntirib ber: avval 3–4 gapda sodda izoh, keyin batafsil tushuntirish, " +
            "hayotiy misollar, ko'p uchraydigan xatolar va 3 ta tekshiruv savoli." + WithExtra(c, "O'qituvchi eslatmasi")),

        new("lessonplan", "Dars ishlanmasi", "Maqsad, natijalar, bosqichlar va vaqt taqsimoti bilan dars ishlanmasi.", true, false, c =>
            Header(c) + "\nShu mavzu bo'yicha to'liq dars ishlanmasini tayyorla:\n" +
            "# Darsning maqsadi (ta'limiy, tarbiyaviy, rivojlantiruvchi)\n# Kutilayotgan natijalar (o'quvchi nimani bila oladi / qila oladi)\n" +
            "# Jihozlar\n# Dars bosqichlari — har birida vaqt (daqiqada), o'qituvchi va o'quvchi faoliyati\n" +
            "# Mustahkamlash savollari\n# Uyga vazifa\n# Baholash mezonlari" + WithExtra(c, "Qo'shimcha talablar")),

        new("goals", "Dars maqsadi va natijalari", "Faqat maqsad va kutilayotgan natijalarni shakllantiradi.", true, false, c =>
            Header(c) + "\nShu dars uchun aniq va o'lchanadigan maqsadlar (3 ta) hamda kutilayotgan natijalarni (4–6 ta, Blum taksonomiyasi fe'llari bilan) yoz."),

        new("test", "Test savollari", "Javob variantlari va kalit bilan test.", true, false, c =>
            Header(c) + $"\nShu mavzu bo'yicha {c.Count} ta test savoli tuz. Murakkablik: {c.Level}. " +
            "Har bir savolda 4 ta variant (A–D) bo'lsin, faqat bittasi to'g'ri; chalg'ituvchi variantlar ishonarli bo'lsin. " +
            "Oxirida alohida \"# Javoblar kaliti\" bo'limida to'g'ri javoblar va har biriga bir gaplik izoh ber."),

        new("levels", "Turli darajadagi savollar", "Oson, o'rta va murakkab savollar.", true, false, c =>
            Header(c) + "\nShu mavzu bo'yicha uch darajadagi ochiq savollar tuz: # Oson (5 ta) # O'rta (5 ta) # Murakkab (3 ta). " +
            "Har bir savolga qisqa namunaviy javob ber."),

        new("practice", "Amaliy topshiriqlar", "Kompyuterda bajariladigan amaliy ishlar.", true, false, c =>
            Header(c) + $"\nShu mavzu bo'yicha {Math.Min(c.Count, 8)} ta amaliy topshiriq tuz (murakkablik: {c.Level}). " +
            "Har birida: shart, kirish/chiqish namunasi (agar dasturlash bo'lsa), baholash mezoni va o'qituvchi uchun yechim yoki yo'riqnoma."),

        new("individual", "Individual mashqlar", "Kuchli va qiyinchilikka uchragan o'quvchilar uchun alohida mashqlar.", true, true, c =>
            Header(c) + "\nO'quvchilar uchun individual mashqlar taklif qil: # Qiyinchilikka uchragan o'quvchilar uchun (4 ta, bosqichma-bosqich) " +
            "# O'rta daraja (4 ta) # Kuchli / olimpiadaga tayyorlanayotgan o'quvchilar uchun (3 ta)." + WithExtra(c, "Guruh haqida o'qituvchi izohi")),

        new("explaincode", "Kodni tushuntirish", "Berilgan kodni qatorma-qator tushuntiradi.", false, true, c =>
            Header(c) + "\nQuyidagi kodni o'quvchiga tushunarli qilib qatorma-qator tushuntir, nima natija berishini ayt va qaysi tushunchalar ishlatilganini sanab o't." +
            WithExtra(c, "Kod")),

        new("reviewcode", "Kodni tekshirish", "Xatolar, chekka holatlar va yaxshilash takliflari.", false, true, c =>
            Header(c) + "\nQuyidagi kodni tekshir: sintaktik va mantiqiy xatolar, chekka holatlar (bo'sh kirish, katta sonlar va h.k.), " +
            "samaradorlik. Har bir muammo uchun sababini va tuzatilgan variantini ko'rsat. Ishonching komil bo'lmagan joyni alohida belgila." +
            WithExtra(c, "Kod")),

        new("slides", "Taqdimot rejasi", "Slaydlar bo'yicha taqdimot rejasi.", true, false, c =>
            Header(c) + "\nShu mavzu bo'yicha 10–12 slayddan iborat taqdimot rejasini tuz: har bir slayd uchun sarlavha, 3–5 ta asosiy fikr va qanday rasm/diagramma qo'yish taklifi."),

        new("summary", "Dars yakuni xulosasi", "Darsni umumlashtiruvchi xulosa va refleksiya savollari.", true, true, c =>
            Header(c) + "\nDars yakunida o'quvchilarga aytiladigan qisqa umumlashtiruvchi xulosa (5–7 gap), asosiy tushunchalar ro'yxati va 3 ta refleksiya savoli tayyorla." +
            WithExtra(c, "Darsda nima bo'ldi (o'qituvchi izohi)")),

        new("analytics", "O'zlashtirish tahlili", "Guruh davomati bo'yicha anonim tahlil va tavsiyalar.", false, true, c =>
            Header(c) + "\nQuyida guruh bo'yicha ANONIM davomat ko'rsatkichlari berilgan (ismlar yo'q). Ularni tahlil qil: umumiy holat, " +
            "e'tibor talab qiladigan holatlar va o'qituvchi uchun 3–5 ta amaliy tavsiya. Ma'lumotda yo'q narsani taxmin qilma." +
            WithExtra(c, "Ma'lumotlar")),

        new("chat", "Erkin savol", "Istalgan savol.", false, true, c =>
            (string.IsNullOrWhiteSpace(Header(c)) ? "" : Header(c) + "\n") + c.Extra.Trim()),
    };

    public static AiTaskTemplate Get(string key) => All.FirstOrDefault(t => t.Key == key) ?? All.Last();

    /// <summary>Suhbat sarlavhasi uchun qisqa matn.</summary>
    public static string MakeTitle(AiTaskTemplate t, AiTaskContext c)
    {
        var basis = !string.IsNullOrWhiteSpace(c.Topic) ? c.Topic : c.Extra;
        basis = (basis ?? "").Replace('\n', ' ').Trim();
        if (basis.Length > 70) basis = basis[..70] + "…";
        return basis.Length == 0 ? t.Title : $"{t.Title}: {basis}";
    }
}

/// <summary>
/// AI ga yuborishdan oldin shaxsiy ma'lumotlarni olib tashlash: o'quvchi ismlari o'rniga "O'quvchi 1, 2 ...".
/// </summary>
public static class Anonymizer
{
    public sealed record StudentStat(string FullName, int Present, int Late, int Excused, int Unexcused, int Total);

    public static string AttendanceSummary(string groupLabel, string period, IReadOnlyList<StudentStat> stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Guruh: {groupLabel}; davr: {period}; o'quvchilar soni: {stats.Count}");
        int i = 0;
        foreach (var s in stats.OrderBy(_ => Guid.NewGuid()))
        {
            i++;
            var pct = s.Total == 0 ? "—" : $"{Math.Round(100.0 * (s.Present + s.Late) / s.Total, 1)}%";
            sb.AppendLine($"O'quvchi {i}: darslar {s.Total}, keldi {s.Present}, kechikdi {s.Late}, sababli {s.Excused}, sababsiz {s.Unexcused}, davomat {pct}");
        }
        return sb.ToString();
    }

    /// <summary>Matnda berilgan ismlar uchraydimi (AI ga yuborishdan oldin ogohlantirish uchun).</summary>
    public static List<string> FindNames(string text, IEnumerable<string> names)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return found;
        foreach (var n in names.Where(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 3).Distinct())
            if (text.Contains(n.Trim(), StringComparison.OrdinalIgnoreCase)) found.Add(n.Trim());
        return found;
    }
}
