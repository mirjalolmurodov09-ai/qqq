using System.Windows;
using System.Windows.Controls;
using AiUstozPro.App.Services;
using AiUstozPro.Application.Security;

namespace AiUstozPro.App.Dialogs;

/// <summary>Kirish oynasi. Foydalanuvchilar bo'lmasa — birinchi administratorni yaratish.</summary>
public sealed class LoginWindow : DialogBase
{
    private readonly bool _setup;
    private readonly TextBox _login = new();
    private readonly TextBox _fullName = new();
    private readonly PasswordBox _password = new();
    private readonly PasswordBox _password2 = new();
    private readonly TextBlock _error;

    public UserSession? Session { get; private set; }

    public LoginWindow() : base("AI Ustoz Pro — kirish", "Kirish", 420)
    {
        _setup = !AppState.Services.Auth.HasAnyUser();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var title = new TextBlock { Text = "AI Ustoz Pro", FontSize = 24, FontWeight = FontWeights.SemiBold };
        Body.Children.Add(title);
        if (_setup)
        {
            Title = "AI Ustoz Pro — birinchi sozlash";
            OkButton.Content = "Yaratish va kirish";
            AddHint("Dastur birinchi marta ishga tushirilmoqda. Administrator hisobini yarating. Parol kamida 8 belgi, harf va raqamdan iborat bo'lsin. Parolni unutmang — uni tiklash uchun boshqa administrator kerak bo'ladi.");
            Add("F.I.Sh.", _fullName);
        }
        else
        {
            AddHint("Login va parolingizni kiriting.");
        }
        Add("Login", _login);
        Add("Parol", _password);
        if (_setup) Add("Parolni takrorlang", _password2);
        _error = AddErrorText();
        Loaded += (_, _) => (_setup ? (Control)_fullName : _login).Focus();
    }

    protected override bool Validate()
    {
        try
        {
            if (_setup)
            {
                if (_password.Password != _password2.Password) { ShowError(_error, "Parollar mos kelmadi."); return false; }
                Session = AppState.Services.Auth.CreateInitialAdministrator(_login.Text, _fullName.Text, _password.Password);
                Log.Info($"Birinchi administrator yaratildi: {Session.Login}");
                return true;
            }
            var r = AppState.Services.Auth.Login(_login.Text, _password.Password);
            if (!r.Success) { ShowError(_error, r.Error); _password.Clear(); return false; }
            Session = r.Session;
            Log.Info($"Kirish: {Session!.Login}");
            return true;
        }
        catch (BusinessRuleException ex)
        {
            ShowError(_error, ex.Message);
            return false;
        }
    }
}
