using System.Globalization;
using System.Text;
using AiUstozPro.Domain;

namespace AiUstozPro.Application.Import;

/// <summary>Excel yoki CSV fayldan o'qilgan jadval: sarlavhalar va satrlar (matn ko'rinishida).</summary>
public sealed class TabularData
{
    public List<string> Headers { get; } = new();
    public List<string[]> Rows { get; } = new();

    public string Cell(int row, int col) => col >= 0 && col < Rows[row].Length ? Rows[row][col].Trim() : "";
}

public static class CsvReader
{
    /// <summary>CSV matnini o'qiydi. Ajratgich (; , yoki TAB) avtomatik aniqlanadi, qo'shtirnoqlar qo'llab-quvvatlanadi.</summary>
    public static TabularData Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n').FirstOrDefault() ?? "";
        char sep = DetectSeparator(firstLine);
        var records = ParseRecords(text, sep);
        var data = new TabularData();
        if (records.Count == 0) return data;
        data.Headers.AddRange(records[0].Select(h => h.Trim()));
        foreach (var r in records.Skip(1))
        {
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            data.Rows.Add(r);
        }
        return data;
    }

    public static TabularData ParseFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 bo'lmasa — Windows-1251 (kirill) yoki 1252 bo'lishi mumkin.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding(1251).GetString(bytes);
        }
        return Parse(text);
    }

    private static char DetectSeparator(string line)
    {
        int semi = line.Count(c => c == ';'), comma = line.Count(c => c == ','), tab = line.Count(c => c == '\t');
        if (tab > semi && tab > comma) return '\t';
        return semi >= comma && semi > 0 ? ';' : ',';
    }

    private static List<string[]> ParseRecords(string text, char sep)
    {
        var records = new List<string[]>();
        var field = new StringBuilder();
        var current = new List<string>();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"') { inQuotes = true; continue; }
            if (c == sep) { current.Add(field.ToString()); field.Clear(); continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                current.Add(field.ToString()); field.Clear();
                records.Add(current.ToArray()); current.Clear();
                continue;
            }
            field.Append(c);
        }
        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString());
            records.Add(current.ToArray());
        }
        return records;
    }

    public static string Escape(string? value, char sep = ';')
    {
        value ??= "";
        if (value.IndexOfAny(new[] { sep, '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

/// <summary>Import maydoni va unga mos keladigan ustun sarlavhalari (sinonimlar).</summary>
public sealed record ImportField(string Key, string Label, bool Required, string[] Synonyms);

public static class ColumnMapper
{
    /// <summary>Sarlavhalar bo'yicha har bir maydonga ustun indeksini taxminiy moslaydi (-1 = topilmadi).</summary>
    public static Dictionary<string, int> AutoMap(IReadOnlyList<string> headers, IEnumerable<ImportField> fields)
    {
        var map = new Dictionary<string, int>();
        var normalized = headers.Select(Normalize).ToList();
        var taken = new HashSet<int>();
        foreach (var f in fields)
        {
            int idx = -1;
            foreach (var syn in f.Synonyms.Select(Normalize))
            {
                idx = normalized.FindIndex(h => h == syn);
                if (idx >= 0 && !taken.Contains(idx)) break;
                idx = -1;
            }
            if (idx < 0)
                foreach (var syn in f.Synonyms.Select(Normalize))
                {
                    idx = syn.Length < 4 ? -1 : normalized.FindIndex(h => h.Length > 0 && h.Contains(syn));
                    if (idx >= 0 && !taken.Contains(idx)) break;
                    idx = -1;
                }
            if (idx >= 0) taken.Add(idx);
            map[f.Key] = idx;
        }
        return map;
    }

    public static string Normalize(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        // O'zbek apostrof variantlarini birxillashtirish: ʻ ʼ ‘ ’ ` → '
        s = s.Replace('ʻ', '\'').Replace('ʼ', '\'').Replace('‘', '\'').Replace('’', '\'').Replace('`', '\'');
        var sb = new StringBuilder();
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == '№') sb.Append(ch);
        return sb.ToString();
    }
}

public sealed class ImportRowResult<T>
{
    public int RowNumber { get; init; }
    public T? Item { get; init; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0 && Item is not null;
}

public static class TopicImport
{
    public static readonly ImportField[] Fields =
    {
        new("order", "Tartib raqami", false, new[] { "№", "t/r", "tr", "tartib", "tartib raqami", "raqam" }),
        new("title", "Mavzu nomi", true, new[] { "mavzu", "mavzu nomi", "mavzular", "dars mavzusi", "topic", "тема" }),
        new("hours", "Soat", true, new[] { "soat", "soati", "ajratilgan soat", "soatlar", "hours", "час", "часы" }),
        new("type", "Mashg'ulot turi", false, new[] { "turi", "mashg'ulot turi", "dars turi", "type", "тип" }),
        new("outcome", "Kutilayotgan natija", false, new[] { "kutilayotgan natija", "natija", "outcome" }),
        new("note", "Izoh", false, new[] { "izoh", "eslatma", "note", "примечание" }),
    };

    public static List<ImportRowResult<CurriculumTopic>> Validate(TabularData data, IReadOnlyDictionary<string, int> map)
    {
        var results = new List<ImportRowResult<CurriculumTopic>>();
        foreach (var f in Fields.Where(f => f.Required))
            if (!map.TryGetValue(f.Key, out var c) || c < 0)
            {
                var r = new ImportRowResult<CurriculumTopic> { RowNumber = 0 };
                r.Errors.Add($"Majburiy ustun tanlanmagan: \"{f.Label}\".");
                results.Add(r);
            }
        if (results.Count > 0) return results;

        int autoOrder = 0;
        for (int i = 0; i < data.Rows.Count; i++)
        {
            string Get(string key) => map.TryGetValue(key, out var c) && c >= 0 ? data.Cell(i, c) : "";
            var title = Get("title");
            var hoursText = Get("hours");
            var orderText = Get("order");
            var errors = new List<string>();
            var warnings = new List<string>();
            autoOrder++;

            if (string.IsNullOrWhiteSpace(title)) errors.Add("Mavzu nomi bo'sh.");
            if (!TryParseInt(hoursText, out var hours) || hours <= 0)
                errors.Add($"Soat noto'g'ri: \"{hoursText}\" (musbat butun son bo'lishi kerak).");
            else if (hours > 40)
                warnings.Add($"Soat juda katta: {hours}. Tekshirib ko'ring.");

            int order = autoOrder;
            if (!string.IsNullOrWhiteSpace(orderText))
            {
                if (TryParseInt(orderText, out var o) && o > 0) order = o;
                else warnings.Add($"Tartib raqami o'qilmadi: \"{orderText}\" — {autoOrder} qo'yildi.");
            }

            var type = ParseType(Get("type"), out var typeWarning);
            if (typeWarning is not null) warnings.Add(typeWarning);

            var row = new ImportRowResult<CurriculumTopic>
            {
                RowNumber = i + 2, // 1-satr sarlavha
                Item = errors.Count == 0 ? new CurriculumTopic
                {
                    OrderNo = order,
                    Title = title.Trim(),
                    Hours = hours,
                    Type = type,
                    ExpectedOutcome = NullIfEmpty(Get("outcome")),
                    Note = NullIfEmpty(Get("note")),
                } : null,
            };
            row.Errors.AddRange(errors);
            row.Warnings.AddRange(warnings);
            results.Add(row);
        }

        var dupOrders = results.Where(r => r.Item is not null).GroupBy(r => r.Item!.OrderNo).Where(g => g.Count() > 1);
        foreach (var g in dupOrders)
            foreach (var r in g)
                r.Warnings.Add($"Tartib raqami {g.Key} takrorlangan — fayldagi tartib saqlanadi.");
        return results;
    }

    public static TopicType ParseType(string text, out string? warning)
    {
        warning = null;
        var t = ColumnMapper.Normalize(text);
        if (t.Length == 0) return TopicType.Theory;
        if (t.StartsWith("naz") && t.Contains("ari")) return TopicType.Theory;   // nazariy
        if (t.StartsWith("ma'r") || t.StartsWith("mar") || t.Contains("лекц") || t.Contains("теор")) return TopicType.Theory;
        if (t.StartsWith("am") || t.Contains("практ") || t.Contains("pract")) return TopicType.Practice;
        if (t.StartsWith("lab") || t.Contains("лаб")) return TopicType.Laboratory;
        if (t.StartsWith("nazorat") || t.Contains("контр") || t.Contains("test")) return TopicType.Control;
        if (t.StartsWith("theor")) return TopicType.Theory;
        warning = $"Mashg'ulot turi tanilmadi: \"{text}\" — \"Nazariy\" qo'yildi.";
        return TopicType.Theory;
    }

    internal static bool TryParseInt(string s, out int value)
    {
        s = (s ?? "").Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && Math.Abs(d - Math.Round(d)) < 1e-9)
        {
            value = (int)Math.Round(d);
            return true;
        }
        return false;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class StudentImport
{
    public static readonly ImportField[] Fields =
    {
        new("last", "Familiya", true, new[] { "familiya", "familiyasi", "фамилия", "last name", "surname" }),
        new("first", "Ism", true, new[] { "ism", "ismi", "имя", "first name", "name" }),
        new("middle", "Otasining ismi", false, new[] { "otasining ismi", "sharifi", "otchestvo", "отчество", "middle name" }),
        new("number", "O'quvchi raqami", false, new[] { "o'quvchi raqami", "raqami", "id", "student id", "номер" }),
    };

    /// <param name="existingNumbers">Tizimda mavjud o'quvchi raqamlari (takrorlanishni oldini olish).</param>
    /// <param name="existingNamesInGroup">Shu guruhdagi mavjud F.I.Sh. (normallashtirilgan) — faqat ogohlantirish uchun.</param>
    public static List<ImportRowResult<Student>> Validate(
        TabularData data, IReadOnlyDictionary<string, int> map,
        ISet<string> existingNumbers, ISet<string> existingNamesInGroup)
    {
        var results = new List<ImportRowResult<Student>>();
        foreach (var f in Fields.Where(f => f.Required))
            if (!map.TryGetValue(f.Key, out var c) || c < 0)
            {
                var r = new ImportRowResult<Student> { RowNumber = 0 };
                r.Errors.Add($"Majburiy ustun tanlanmagan: \"{f.Label}\".");
                results.Add(r);
            }
        if (results.Count > 0) return results;

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>();
        for (int i = 0; i < data.Rows.Count; i++)
        {
            string Get(string key) => map.TryGetValue(key, out var c) && c >= 0 ? data.Cell(i, c) : "";
            var last = Get("last"); var first = Get("first"); var middle = Get("middle"); var number = Get("number");
            var row = new ImportRowResult<Student> { RowNumber = i + 2, Item = null };
            if (string.IsNullOrWhiteSpace(last)) row.Errors.Add("Familiya bo'sh.");
            if (string.IsNullOrWhiteSpace(first)) row.Errors.Add("Ism bo'sh.");
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (existingNumbers.Contains(number)) row.Errors.Add($"O'quvchi raqami {number} tizimda allaqachon mavjud.");
                else if (!seenNumbers.Add(number)) row.Errors.Add($"O'quvchi raqami {number} faylda takrorlangan.");
            }
            var nameKey = NameKey(last, first, middle);
            if (row.Errors.Count == 0)
            {
                if (existingNamesInGroup.Contains(nameKey))
                    row.Warnings.Add("Guruhda xuddi shunday F.I.Sh. li o'quvchi bor. Bu boshqa o'quvchi bo'lsa — davom eting, aks holda satrni o'chiring.");
                else if (!seenNames.Add(nameKey))
                    row.Warnings.Add("Faylda xuddi shunday F.I.Sh. takrorlangan — ikki xil o'quvchi ekanini tekshiring.");
            }
            var result = new ImportRowResult<Student>
            {
                RowNumber = row.RowNumber,
                Item = row.Errors.Count == 0 ? new Student
                {
                    LastName = last.Trim(),
                    FirstName = first.Trim(),
                    MiddleName = string.IsNullOrWhiteSpace(middle) ? null : middle.Trim(),
                    StudentNumber = string.IsNullOrWhiteSpace(number) ? null : number.Trim(),
                } : null,
            };
            result.Errors.AddRange(row.Errors);
            result.Warnings.AddRange(row.Warnings);
            results.Add(result);
        }
        return results;
    }

    public static string NameKey(string? last, string? first, string? middle)
        => string.Join("|", new[] { last, first, middle }.Select(x => ColumnMapper.Normalize(x ?? "")));
}
