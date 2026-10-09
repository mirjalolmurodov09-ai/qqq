using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Import;
using AiUstozPro.Application.Security;
using AiUstozPro.Infrastructure.Import;

namespace AiUstozPro.App.Dialogs;

public sealed class PreviewRow
{
    public int RowNumber { get; init; }
    public bool IsValid { get; init; }
    public string State => IsValid ? (Messages.Length > 0 ? "Ogohlantirish" : "To'g'ri") : "Xato";
    public string Messages { get; init; } = "";
    public string[] Values { get; init; } = Array.Empty<string>();
    public object? Item { get; init; }
    public string V0 => Values.Length > 0 ? Values[0] : "";
    public string V1 => Values.Length > 1 ? Values[1] : "";
    public string V2 => Values.Length > 2 ? Values[2] : "";
    public string V3 => Values.Length > 3 ? Values[3] : "";
    public string V4 => Values.Length > 4 ? Values[4] : "";
    public string V5 => Values.Length > 5 ? Values[5] : "";
}

/// <summary>
/// Excel/CSV import oynasi: fayl tanlash → ustunlarni moslashtirish → oldindan ko'rish va xatolar → import.
/// </summary>
public sealed class ImportDialog : DialogBase
{
    private readonly ImportField[] _fields;
    private readonly Func<TabularData, IReadOnlyDictionary<string, int>, List<PreviewRow>> _validate;
    private readonly Dictionary<string, ComboBox> _mapBoxes = new();
    private readonly StackPanel _mapPanel = new();
    private readonly DataGrid _grid = new() { Height = 320, IsReadOnly = true };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _fileText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private TabularData? _data;
    private List<PreviewRow> _rows = new();

    public CheckBox? OptionBox { get; }
    public List<object> ValidItems => _rows.Where(r => r.IsValid && r.Item is not null).Select(r => r.Item!).ToList();

    public ImportDialog(string title, string hint, ImportField[] fields,
        Func<TabularData, IReadOnlyDictionary<string, int>, List<PreviewRow>> validate, string? optionText = null, bool optionDefault = false)
        : base(title, "Import qilish", 900)
    {
        _fields = fields;
        _validate = validate;
        OkButton.IsEnabled = false;

        AddHint(hint);
        var filePanel = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        var choose = new Button { Content = "Faylni tanlash (.xlsx, .csv)…" };
        choose.Click += (_, _) => ChooseFile();
        DockPanel.SetDock(choose, Dock.Left);
        filePanel.Children.Add(choose);
        filePanel.Children.Add(_fileText);
        Body.Children.Add(filePanel);

        var mapTitle = new TextBlock { Text = "Ustunlarni moslashtirish", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        Body.Children.Add(mapTitle);
        Body.Children.Add(_mapPanel);

        if (optionText is not null)
        {
            OptionBox = new CheckBox { Content = optionText, IsChecked = optionDefault, Margin = new Thickness(0, 10, 0, 0) };
            Body.Children.Add(OptionBox);
        }

        var prevTitle = new TextBlock { Text = "Oldindan ko'rish", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        Body.Children.Add(prevTitle);
        BuildColumns();
        Body.Children.Add(_grid);
        Body.Children.Add(_summary);
    }

    private void BuildColumns()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridTextColumn { Header = "Satr", Binding = new Binding(nameof(PreviewRow.RowNumber)), Width = 50 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Holat", Binding = new Binding(nameof(PreviewRow.State)), Width = 100 });
        for (int i = 0; i < Math.Min(6, _fields.Length); i++)
            _grid.Columns.Add(new DataGridTextColumn { Header = _fields[i].Label, Binding = new Binding("V" + i), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Xato / ogohlantirish", Binding = new Binding(nameof(PreviewRow.Messages)), Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } },
        });
        _grid.RowHeight = double.NaN;
    }

    private void ChooseFile()
    {
        var path = Ui.OpenFile("Excel yoki CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Barcha fayllar (*.*)|*.*");
        if (path is null) return;
        try
        {
            _data = TabularFileReader.Read(path);
        }
        catch (BusinessRuleException ex) { Ui.Warn(ex.Message); return; }
        catch (Exception ex) { Log.Error(ex, "Import faylini o'qish"); Ui.Warn($"Faylni o'qib bo'lmadi: {ex.Message}"); return; }

        _fileText.Text = $"  {System.IO.Path.GetFileName(path)} — {_data.Rows.Count} ta satr, {_data.Headers.Count} ta ustun";
        var auto = ColumnMapper.AutoMap(_data.Headers, _fields);
        _mapPanel.Children.Clear();
        _mapBoxes.Clear();
        var options = new List<string> { "— tanlanmagan —" };
        options.AddRange(_data.Headers.Select((h, i) => $"{i + 1}. {(string.IsNullOrWhiteSpace(h) ? "(sarlavhasiz)" : h)}"));
        var wrap = new WrapPanel();
        foreach (var f in _fields)
        {
            var sp = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 12, 6) };
            var lbl = new TextBlock { Text = f.Label + (f.Required ? " *" : "") };
            lbl.SetResourceReference(StyleProperty, "Label");
            sp.Children.Add(lbl);
            var cb = new ComboBox { ItemsSource = options, SelectedIndex = auto.TryGetValue(f.Key, out var idx) && idx >= 0 ? idx + 1 : 0 };
            cb.SelectionChanged += (_, _) => Revalidate();
            sp.Children.Add(cb);
            _mapBoxes[f.Key] = cb;
            wrap.Children.Add(sp);
        }
        _mapPanel.Children.Add(wrap);
        Revalidate();
    }

    private void Revalidate()
    {
        if (_data is null) return;
        var map = _mapBoxes.ToDictionary(kv => kv.Key, kv => kv.Value.SelectedIndex - 1);
        try
        {
            _rows = _validate(_data, map);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import tekshiruvi");
            _rows = new List<PreviewRow> { new() { RowNumber = 0, IsValid = false, Messages = ex.Message } };
        }
        _grid.ItemsSource = _rows;
        int valid = _rows.Count(r => r.IsValid), invalid = _rows.Count(r => !r.IsValid), warn = _rows.Count(r => r.IsValid && r.Messages.Length > 0);
        _summary.Text = $"To'g'ri: {valid} ta (shundan ogohlantirish bilan: {warn}), xato: {invalid} ta. Xato satrlar import qilinmaydi — ularni faylda tuzatib, qayta tanlashingiz mumkin.";
        OkButton.IsEnabled = valid > 0;
    }

    protected override bool Validate()
    {
        var invalid = _rows.Count(r => !r.IsValid);
        if (invalid > 0 && !Ui.Confirm($"{invalid} ta satrda xato bor va ular import qilinmaydi. Qolgan {_rows.Count - invalid} ta satrni import qilasizmi?"))
            return false;
        return true;
    }
}
