using System.Windows;
using System.Windows.Input;

namespace AiUstozPro.App.Views;

/// <summary>Proyektor/ikkinchi ekran uchun katta ko'rinish. F11 — to'liq ekran, Esc — chiqish.</summary>
public partial class PresentationWindow : Window
{
    public PresentationWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
            {
                var full = WindowStyle != WindowStyle.None;
                WindowStyle = full ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                WindowState = full ? WindowState.Maximized : WindowState.Normal;
            }
            else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
        };
    }
}
