using System.Globalization;
using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using ClosedXML.Excel;

namespace AiUstozPro.Infrastructure.Import;

/// <summary>Excel (.xlsx) yoki CSV faylni <see cref="TabularData"/> ko'rinishida o'qiydi.</summary>
public static class TabularFileReader
{
    public static TabularData Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".csv" or ".txt" => CsvReader.ParseFile(path),
                ".xlsx" or ".xlsm" => ReadExcel(path),
                ".xls" => throw new BusinessRuleException("Eski .xls formati qo'llab-quvvatlanmaydi. Faylni Excel'da .xlsx sifatida saqlang."),
                _ => throw new BusinessRuleException("Faqat .xlsx yoki .csv fayllar qabul qilinadi."),
            };
        }
        catch (IOException ex)
        {
            throw new BusinessRuleException($"Faylni o'qib bo'lmadi (boshqa dasturda ochiq bo'lishi mumkin): {ex.Message}");
        }
    }

    private static TabularData ReadExcel(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.FirstOrDefault() ?? throw new BusinessRuleException("Excel faylida varaq topilmadi.");
        var used = ws.RangeUsed();
        var data = new TabularData();
        if (used is null) return data;

        int firstRow = used.FirstRow().RowNumber(), lastRow = used.LastRow().RowNumber();
        int firstCol = used.FirstColumn().ColumnNumber(), lastCol = used.LastColumn().ColumnNumber();

        // Sarlavha qatori: kamida ikkita to'ldirilgan katakka ega birinchi qator (sarlavha ustidagi nom satrlarini o'tkazib yuborish uchun).
        int headerRow = firstRow;
        for (int r = firstRow; r <= Math.Min(lastRow, firstRow + 10); r++)
        {
            int filled = 0;
            for (int c = firstCol; c <= lastCol; c++) if (!ws.Cell(r, c).IsEmpty()) filled++;
            if (filled >= 2) { headerRow = r; break; }
        }
        for (int c = firstCol; c <= lastCol; c++) data.Headers.Add(CellText(ws.Cell(headerRow, c)));
        for (int r = headerRow + 1; r <= lastRow; r++)
        {
            var row = new string[lastCol - firstCol + 1];
            bool any = false;
            for (int c = firstCol; c <= lastCol; c++)
            {
                row[c - firstCol] = CellText(ws.Cell(r, c));
                if (row[c - firstCol].Length > 0) any = true;
            }
            if (any) data.Rows.Add(row);
        }
        return data;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        var v = cell.Value;
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (v.IsDateTime) return v.GetDateTime().ToString("dd.MM.yyyy");
        return cell.GetString().Trim();
    }
}
