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
