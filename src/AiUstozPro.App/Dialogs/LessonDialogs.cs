using System.Windows.Controls;
using AiUstozPro.App.Services;

namespace AiUstozPro.App.Dialogs;

/// <summary>Darsni ko'chirish yoki qo'shimcha dars qo'shish oynasi.</summary>
public sealed class LessonEditDialog : DialogBase
{
    private readonly DatePicker _date = new();
    private readonly TextBox _number = new();
    private readonly TextBox _start = new();
    private readonly TextBox _end = new();
    private readonly TextBox _room = new();
    private readonly TextBox _hours = new();
    private readonly TextBox _reason = new();
    private readonly TextBlock _error;
    private readonly bool _extra;

    public DateOnly Date { get; private set; }
    public int LessonNumber { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
    public string? Room => string.IsNullOrWhiteSpace(_room.Text) ? null : _room.Text.Trim();
    public int Hours { get; private set; }
    public string Reason => _reason.Text.Trim();

    public LessonEditDialog(string title, bool extra, DateOnly date, int lessonNumber, TimeOnly start, TimeOnly end, string? room, int hours)
        : base(title, extra ? "Qo'shish" : "Ko'chirish")
    {
        _extra = extra;
        _date.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        _number.Text = lessonNumber.ToString();
        _start.Text = start.ToString("HH:mm");
        _end.Text = end.ToString("HH:mm");
        _room.Text = room ?? "";
        _hours.Text = hours.ToString();
        Add("Sana", _date);
        Add("Dars raqami", _number);
        Add("Boshlanish vaqti (SS:dd)", _start);
        Add("Tugash vaqti (SS:dd)", _end);
        if (extra)
        {
            Add("Xona", _room);
            Add("Akademik soat", _hours);
        }
        Add("Sabab *", _reason);
        AddHint(extra ? "Qo'shimcha dars avtomatik qayta hisoblashda o'chirilmaydi." : "Ko'chirilgan dars asl sanasi bilan bog'liq holda saqlanadi va qayta hisoblashda o'zgartirilmaydi.");
        _error = AddErrorText();
    }

    protected override bool Validate()
    {
        if (_date.SelectedDate is not { } d) { ShowError(_error, "Sanani tanlang."); return false; }
        if (!int.TryParse(_number.Text, out var n) || n < 1 || n > 12) { ShowError(_error, "Dars raqami 1–12 oralig'ida bo'lsin."); return false; }
        if (!Ui.TryParseTime(_start.Text, out var s) || !Ui.TryParseTime(_end.Text, out var e) || e <= s) { ShowError(_error, "Vaqtlar noto'g'ri (masalan 08:30 va 09:50)."); return false; }
        int h = 2;
        if (_extra && (!int.TryParse(_hours.Text, out h) || h < 1 || h > 8)) { ShowError(_error, "Akademik soat 1–8 oralig'ida bo'lsin."); return false; }
        if (Reason.Length == 0) { ShowError(_error, "Sababni kiriting."); return false; }
        Date = DateOnly.FromDateTime(d); LessonNumber = n; Start = s; End = e; Hours = h;
        return true;
    }
}
