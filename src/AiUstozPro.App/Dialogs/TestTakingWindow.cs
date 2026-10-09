using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AiUstozPro.App.Dialogs;

public sealed record TakingQuestion(string Text, IReadOnlyList<string> Options, int Points);

/// <summary>
/// O'quvchi testni o'qituvchi kompyuterida topshiradigan oyna: taymer, savollar, avtomatik topshirish.
/// Natija bahosi o'quvchiga ko'rsatilmaydi — o'qituvchi tasdiqlaydi.
/// </summary>
public sealed class TestTakingWindow : Window
{
    private readonly IReadOnlyList<TakingQuestion> _questions;
    private readonly int?[] _selected;
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly TimeSpan? _limit;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _timerText = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _progress = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
    private bool _submitted;

    public IReadOnlyList<int?> Selections => _selected;
    public int DurationSeconds { get; private set; }
    public bool TimedOut { get; private set; }
    /// <summary>Test topshirildi (tugma, vaqt tugashi yoki oynani yopishda tasdiqlash orqali).</summary>
    public bool Completed => _submitted;

    public TestTakingWindow(string title, string studentName, int variant, IReadOnlyList<TakingQuestion> questions, int timeLimitMinutes)
    {
        _questions = questions;
        _selected = new int?[questions.Count];
        _limit = timeLimitMinutes > 0 ? TimeSpan.FromMinutes(timeLimitMinutes) : null;
        Title = $"{title} — {studentName}";
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 16;
        SetResourceReference(BackgroundProperty, "WindowBg");
        SetResourceReference(ForegroundProperty, "TextPrimary");

        var header = new DockPanel { Margin = new Thickness(32, 20, 32, 12) };
        var submit = new Button { Content = "Testni topshirish", Padding = new Thickness(20, 10, 20, 10), FontSize = 16 };
        submit.SetResourceReference(StyleProperty, "PrimaryButton");
        submit.Click += (_, _) => Submit(confirm: true);
        DockPanel.SetDock(submit, Dock.Right);
        header.Children.Add(submit);
        var info = new StackPanel { Orientation = Orientation.Horizontal };
        info.Children.Add(new TextBlock { Text = $"{studentName} · {variant}-variant", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) });
        info.Children.Add(_timerText);
        info.Children.Add(_progress);
        header.Children.Add(info);

        var list = new StackPanel { Margin = new Thickness(32, 0, 32, 32), MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceBg");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {q.Text}" + (q.Points > 1 ? $"  ({q.Points} ball)" : ""),
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10),
            });
            for (int j = 0; j < q.Options.Count; j++)
            {
                int qi = i, oj = j;
                var rb = new RadioButton
                {
                    GroupName = "q" + i,
                    Content = new TextBlock { Text = $"{AiUstozPro.Application.Testing.AnswerSheet.Letter(j)}) {q.Options[j]}", TextWrapping = TextWrapping.Wrap },
                    Margin = new Thickness(0, 4, 0, 4),
                };
                rb.SetResourceReference(ForegroundProperty, "TextPrimary");
                rb.Checked += (_, _) => { _selected[qi] = oj; UpdateProgress(); };
                sp.Children.Add(rb);
            }
            card.Child = sp;
            list.Children.Add(card);
        }

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Tick(); UpdateProgress(); _timer.Start(); };
        Closing += (_, e) =>
        {
            if (_submitted) return;
            if (MessageBox.Show(this, "Testni hozir topshirasizmi? Belgilanmagan savollar javobsiz hisoblanadi.", "AI Ustoz Pro",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Finish(fromClosing: true);
            else e.Cancel = true;
        };
    }

    private void Tick()
    {
        var elapsed = DateTime.UtcNow - _started;
        if (_limit is { } limit)
        {
            var left = limit - elapsed;
            if (left <= TimeSpan.Zero)
            {
                TimedOut = true;
                _timerText.Text = "Vaqt tugadi";
                Finish();
                return;
            }
            _timerText.Text = $"Qolgan vaqt: {(int)left.TotalMinutes:00}:{left.Seconds:00}";
            if (left.TotalSeconds < 60) _timerText.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        }
        else _timerText.Text = $"Vaqt: {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateProgress() => _progress.Text = $"Javob berildi: {_selected.Count(s => s is not null)} / {_questions.Count}";

    private void Submit(bool confirm)
    {
        var unanswered = _selected.Count(s => s is null);
        if (confirm && MessageBox.Show(this,
                unanswered > 0 ? $"{unanswered} ta savolga javob berilmagan. Baribir topshirasizmi?" : "Testni topshirasizmi?",
                "AI Ustoz Pro", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Finish();
    }

    private void Finish(bool fromClosing = false)
    {
        if (_submitted) return;
        _submitted = true;
        _timer.Stop();
        DurationSeconds = (int)Math.Round((DateTime.UtcNow - _started).TotalSeconds);
        if (!fromClosing) DialogResult = true;
    }
}
