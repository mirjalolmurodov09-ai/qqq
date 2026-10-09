using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AiUstozPro.Infrastructure.Reports;

/// <summary>Word (.docx) hujjatlarini yaratish (Open XML SDK, MIT litsenziyasi).</summary>
public static class DocxWriter
{
    private const string Font = "Times New Roman";

    public static void WriteTable(ReportTable t, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        body.Append(Para(t.Title, bold: true, size: 28, align: JustificationValues.Center));
        foreach (var s in t.SubtitleLines) body.Append(Para(s, size: 22));
        body.Append(Para(""));

        if (t.Columns.Count > 0)
        {
            var table = new Table();
            table.Append(new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
            var header = new TableRow(new TableRowProperties(new TableHeader()));
            foreach (var c in t.Columns) header.Append(Cell(c, bold: true, shade: true));
            table.Append(header);
            foreach (var r in t.Rows)
            {
                var row = new TableRow();
                for (int i = 0; i < t.Columns.Count; i++) row.Append(Cell(i < r.Length ? r[i] ?? "" : ""));
                table.Append(row);
            }
            body.Append(table);
        }
        body.Append(Para(""));
        foreach (var f in t.FooterLines) body.Append(Para(f, italic: true, size: 20));
        body.Append(PageSetup(t.Landscape));
        main.Document.Save();
    }

    /// <summary>
    /// Oddiy belgilangan matnni Word ga aylantiradi: "# " sarlavha, "- " / "* " ro'yxat, "```" kod bloki, "**qalin**".
    /// </summary>
    public static void WriteText(string title, IEnumerable<string> meta, string text, string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);
        body.Append(Para(title, bold: true, size: 28, align: JustificationValues.Center));
        foreach (var m in meta) body.Append(Para(m, italic: true, size: 20));
        body.Append(Para(""));

        bool inCode = false;
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode) { body.Append(Para(line, size: 20, font: "Consolas")); continue; }
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var level = trimmed.TakeWhile(c => c == '#').Count();
                body.Append(Para(trimmed[level..].Trim(), bold: true, size: level <= 1 ? 26 : 24, spaceBefore: 160));
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                body.Append(Para("• " + trimmed[2..], size: 24, indent: 360));
            else
                body.Append(Para(line, size: 24));
        }
        body.Append(PageSetup(false));
        main.Document.Save();
    }

    private static Paragraph Para(string text, bool bold = false, bool italic = false, int size = 24,
        JustificationValues? align = null, string font = Font, int indent = 0, int spaceBefore = 0)
    {
        var pPr = new ParagraphProperties(new SpacingBetweenLines { After = "80", Before = spaceBefore.ToString() });
        // Open XML sxemasi tartibi: spacing → ind → jc.
        if (indent > 0) pPr.Append(new Indentation { Left = indent.ToString() });
        if (align is { } a) pPr.Append(new Justification { Val = a });
        var p = new Paragraph(pPr);
        foreach (var (segment, segBold) in SplitBold(text))
        {
            // Sxema tartibi: rFonts → b → i → sz.
            var rPr = new RunProperties(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font });
            if (bold || segBold) rPr.Append(new Bold());
            if (italic) rPr.Append(new Italic());
            rPr.Append(new FontSize { Val = size.ToString() });
            p.Append(new Run(rPr, new Text(segment) { Space = SpaceProcessingModeValues.Preserve }));
        }
        return p;
    }

    private static IEnumerable<(string, bool)> SplitBold(string text)
    {
        var parts = (text ?? "").Split("**");
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0 || parts.Length == 1) yield return (parts[i], i % 2 == 1);
    }

    private static TableCell Cell(string text, bool bold = false, bool shade = false)
    {
        var cell = new TableCell();
        var props = new TableCellProperties();
        if (shade) props.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = "E8EEF7", Color = "auto" });
        cell.Append(props);
        cell.Append(Para(text, bold: bold, size: 20));
        return cell;
    }

    private static SectionProperties PageSetup(bool landscape)
    {
        var size = landscape
            ? new PageSize { Width = 16838U, Height = 11906U, Orient = PageOrientationValues.Landscape }
            : new PageSize { Width = 11906U, Height = 16838U };
        return new SectionProperties(size, new PageMargin { Top = 1000, Bottom = 1000, Left = 1000U, Right = 1000U });
    }
}
