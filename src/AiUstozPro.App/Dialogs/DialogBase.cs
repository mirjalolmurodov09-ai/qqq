using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AiUstozPro.App.Dialogs;

/// <summary>Kod orqali quriladigan oddiy modal oynalar uchun asos.</summary>
public abstract class DialogBase : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(20, 16, 20, 8) };
    protected readonly Button OkButton;
    protected readonly Button CancelButton;

    protected DialogBase(string title, string okText = "Saqlash", double width = 440)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SurfaceBg");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;

        OkButton = new Button { Content = okText, IsDefault = true, MinWidth = 100 };
        OkButton.SetResourceReference(StyleProperty, "PrimaryButton");
        OkButton.Click += (_, _) => { if (Validate()) DialogResult = true; };
        CancelButton = new Button { Content = "Bekor qilish", IsCancel = true, MinWidth = 100, Margin = new Thickness(0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 16) };
        buttons.Children.Add(OkButton);
        buttons.Children.Add(CancelButton);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 });
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    protected virtual bool Validate() => true;

    protected TextBlock AddLabel(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(StyleProperty, "Label");
        Body.Children.Add(t);
        return t;
    }

    protected TextBlock AddHint(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        t.SetResourceReference(StyleProperty, "Hint");
        Body.Children.Add(t);
        return t;
    }

    protected T Add<T>(string? label, T control) where T : UIElement
    {
        if (label is not null) AddLabel(label);
        Body.Children.Add(control);
        return control;
    }

    protected void ShowError(TextBlock target, string? message)
    {
        target.Text = message ?? "";
        target.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    protected TextBlock AddErrorText()
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        t.SetResourceReference(ForegroundProperty, "Danger");
        Body.Children.Add(t);
        return t;
    }
}

public sealed class InputDialog : DialogBase
{
    private readonly TextBox _box;
    private readonly bool _required;
    private readonly TextBlock _error;

    public InputDialog(string title, string prompt, string initial, bool required) : base(title, "OK")
    {
        _required = required;
        var p = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        Body.Children.Add(p);
        _box = Add(null, new TextBox { Text = initial, AcceptsReturn = false });
        _error = AddErrorText();
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public string Value => _box.Text.Trim();

    protected override bool Validate()
    {
        if (_required && Value.Length == 0) { ShowError(_error, "Bu maydon to'ldirilishi shart."); return false; }
        return true;
    }
}

/// <summary>Parol so'rash (ixtiyoriy ravishda takrorlash bilan).</summary>
public sealed class PasswordDialog : DialogBase
{
    private readonly PasswordBox _p1 = new();
    private readonly PasswordBox _p2 = new();
    private readonly bool _confirm;
    private readonly TextBlock _error;

    public PasswordDialog(string title, string prompt, bool confirm) : base(title, "OK")
    {
        _confirm = confirm;
        Body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        Add("Parol", _p1);
        if (confirm) Add("Parolni takrorlang", _p2);
        _error = AddErrorText();
        Loaded += (_, _) => _p1.Focus();
    }

    public string Value => _p1.Password;

    protected override bool Validate()
    {
        if (_p1.Password.Length < 8) { ShowError(_error, "Parol kamida 8 belgidan iborat bo'lsin."); return false; }
        if (_confirm && _p1.Password != _p2.Password) { ShowError(_error, "Parollar mos kelmadi."); return false; }
        return true;
    }
}

/// <summary>AI javobini tahrirlash va o'qituvchi tasdig'i.</summary>
public sealed class ReviewDialog : DialogBase
{
    private readonly TextBox _text;
    private readonly CheckBox _checked = new() { Content = "Matnni o'qib chiqdim, faktlarni tekshirdim va to'g'riligiga javob beraman", Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _error;

    public ReviewDialog(string text) : base("AI javobini tekshirish va tasdiqlash", "Tasdiqlash", 860)
    {
        AddHint("AI xato qilishi mumkin. Matnni tahrirlang va tekshiring. Faqat tasdiqlangan matn Word hujjatiga \"o'qituvchi tekshirgan\" belgisi bilan chiqariladi.");
        _text = new TextBox
        {
            Text = text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Height = 460,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        Body.Children.Add(_text);
        Body.Children.Add(_checked);
        _error = AddErrorText();
    }

    public string Value => _text.Text;

    protected override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_text.Text)) { ShowError(_error, "Matn bo'sh."); return false; }
        if (_checked.IsChecked != true) { ShowError(_error, "Tasdiqlash uchun belgini qo'ying."); return false; }
        return true;
    }
}
