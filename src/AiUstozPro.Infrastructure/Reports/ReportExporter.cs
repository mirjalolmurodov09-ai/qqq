using System.Globalization;
using System.Text;
using AiUstozPro.Application.Import;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AiUstozPro.Infrastructure.Reports;

public enum ExportFormat { Excel, Csv, Pdf, Word }

public static class ReportExporter
{
    static ReportExporter()
    {
        // QuestPDF Community litsenziyasi: yillik daromadi 1 mln USD dan kam tashkilotlar va shaxslar uchun bepul.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.Excel => ".xlsx",
        ExportFormat.Csv => ".csv",
        ExportFormat.Pdf => ".pdf",
        ExportFormat.Word => ".docx",
        _ => "",
    };

    public static void Export(ReportTable table, ExportFormat format, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Avval vaqtinchalik faylga yozamiz — xato bo'lsa mavjud fayl buzilmaydi.
        var tmp = Path.Combine(dir ?? "", $"~{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}{Extension(format)}");
        switch (format)
        {
            case ExportFormat.Excel: ToExcel(table, tmp); break;
            case ExportFormat.Csv: ToCsv(table, tmp); break;
            case ExportFormat.Pdf: ToPdf(table, tmp); break;
            case ExportFormat.Word: DocxWriter.WriteTable(table, tmp); break;
        }
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Fayl kengaytmasi bo'yicha format (.xlsx/.pdf/.csv/.docx).</summary>
    public static ExportFormat FormatFromPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => ExportFormat.Pdf,
        ".csv" => ExportFormat.Csv,
        ".docx" => ExportFormat.Word,
        _ => ExportFormat.Excel,
    };

    public const string SaveFilter = "Excel (*.xlsx)|*.xlsx|Word (*.docx)|*.docx|PDF (*.pdf)|*.pdf|CSV (*.csv)|*.csv";

    public static void ToCsv(ReportTable t, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvReader.Escape(t.Title));
        foreach (var s in t.SubtitleLines) sb.AppendLine(CsvReader.Escape(s));
        sb.AppendLine();
        sb.AppendLine(string.Join(";", t.Columns.Select(c => CsvReader.Escape(c))));
        foreach (var r in t.Rows) sb.AppendLine(string.Join(";", r.Select(c => CsvReader.Escape(c))));
        sb.AppendLine();
        foreach (var f in t.FooterLines) sb.AppendLine(CsvReader.Escape(f));
        // UTF-8 BOM — Excel o'zbekcha belgilarni to'g'ri ochishi uchun.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static void ToExcel(ReportTable t, string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(t.Title));
        int row = 1;
        var colCount = Math.Max(1, t.Columns.Count);
        ws.Cell(row, 1).Value = t.Title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row++;
        foreach (var s in t.SubtitleLines) { ws.Cell(row, 1).Value = s; row++; }
        row++;
        int headerRow = row;
        for (int c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = t.Columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        row++;
        foreach (var r in t.Rows)
        {
            for (int c = 0; c < r.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                var v = r[c] ?? "";
                // Faqat sof butun sonlarni raqam sifatida yozamiz (sanalar matn bo'lib qoladi — dd.MM.yyyy).
                if (v.Length > 0 && v.Length < 10 && v.All(char.IsDigit) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    cell.Value = n;
                else
                    cell.Value = v;
            }
            row++;
        }
        if (t.Columns.Count > 0 && row - 1 >= headerRow)
        {
            var range = ws.Range(headerRow, 1, row - 1, colCount);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }
        row++;
        foreach (var f in t.FooterLines) { ws.Cell(row, 1).Value = f; ws.Cell(row, 1).Style.Font.Italic = true; row++; }
        ws.Columns(1, colCount).AdjustToContents(headerRow, row, 8, 60);
        ws.SheetView.FreezeRows(headerRow);
        wb.SaveAs(path);
    }

    public static void ToPdf(ReportTable t, string path)
    {
        var widths = t.Widths.Count == t.Columns.Count ? t.Widths : t.Columns.Select(_ => 1f).ToList();
        float fontSize = t.Columns.Count > 20 ? 6.5f : t.Columns.Count > 12 ? 7.5f : 9f;
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(t.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(fontSize));
                page.Header().Column(col =>
                {
                    col.Item().Text(t.Title).FontSize(14).Bold();
                    foreach (var s in t.SubtitleLines) col.Item().Text(s).FontSize(10);
                    col.Item().PaddingBottom(6);
                });
                page.Content().Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            foreach (var w in widths) cd.RelativeColumn(w);
                        });
                        table.Header(h =>
                        {
                            foreach (var c in t.Columns)
                                h.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).Padding(2).Text(c).Bold();
                        });
                        foreach (var r in t.Rows)
                            for (int i = 0; i < t.Columns.Count; i++)
                                table.Cell().Border(0.5f).Padding(2).Text(i < r.Length ? r[i] ?? "" : "");
                    });
                    foreach (var f in t.FooterLines)
                        col.Item().PaddingTop(4).Text(f).Italic();
                });
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("AI Ustoz Pro · sahifa ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Belgilangan matnni (# sarlavha, **qalin**, - ro'yxat) Word yoki PDF ga chiqaradi.</summary>
    public static void ExportText(string title, IReadOnlyList<string> meta, string text, ExportFormat format, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (format == ExportFormat.Word) { DocxWriter.WriteText(title, meta, text, path); return; }
        if (format != ExportFormat.Pdf) throw new ArgumentException("Matn faqat Word yoki PDF ga chiqariladi.");
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(11));
                page.Content().Column(col =>
                {
                    col.Item().Text(title).FontSize(15).Bold();
                    foreach (var m in meta) col.Item().Text(m).FontSize(9).Italic();
                    col.Item().PaddingBottom(6);
                    foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                    {
                        var line = raw.TrimEnd();
                        var trimmed = line.TrimStart();
                        if (trimmed.StartsWith('#'))
                        {
                            var level = trimmed.TakeWhile(c => c == '#').Count();
                            col.Item().PaddingTop(8).Text(trimmed[level..].Trim()).FontSize(level <= 1 ? 13 : 12).Bold();
                        }
                        else if (trimmed.StartsWith("```")) continue;
                        else if (line.Length == 0) col.Item().Height(6);
                        else
                        {
                            var indent = line.Length - trimmed.Length;
                            col.Item().PaddingLeft(Math.Min(indent, 8) * 4).Text(t =>
                            {
                                var parts = trimmed.Split("**");
                                for (int i = 0; i < parts.Length; i++)
                                {
                                    if (parts[i].Length == 0) continue;
                                    var span = t.Span(parts[i]);
                                    if (i % 2 == 1) span.Bold();
                                }
                            });
                        }
                    }
                });
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("AI Ustoz Pro · ");
                    x.CurrentPageNumber();
                });
            });
        }).GeneratePdf();
        File.WriteAllBytes(path, bytes);
    }

    private static string SafeSheetName(string s)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = new string((s ?? "Hisobot").Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        if (clean.Length == 0) clean = "Hisobot";
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
