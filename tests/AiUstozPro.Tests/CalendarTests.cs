using AiUstozPro.Application.Calendar;
using AiUstozPro.Domain;

namespace AiUstozPro.Tests;

public class CalendarTests
{
    internal static AcademicCalendar Cal(DateOnly from, DateOnly to, params DayOfWeek[] working)
        => new()
        {
            Id = 1, Name = "test", StartDate = from, EndDate = to,
            WorkingDaysMask = working.Length == 0
                ? AcademicCalendar.MaskOf(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)
                : AcademicCalendar.MaskOf(working),
        };

    internal static TimetableEntry Entry(int id, DayOfWeek dow, int lessonNo = 1, int group = 1, int subject = 1)
        => new()
        {
            Id = id, AcademicCalendarId = 1, GroupId = group, SubjectId = subject, TeacherUserId = 1,
            DayOfWeek = dow, LessonNumber = lessonNo, StartTime = new TimeOnly(8, 30), EndTime = new TimeOnly(9, 50), AcademicHours = 2,
        };

    private static CalendarException Ex(CalendarExceptionKind kind, DateOnly start, DateOnly? end = null, DayOfWeek? worksAs = null, int? group = null, int? subject = null)
        => new() { AcademicCalendarId = 1, Kind = kind, StartDate = start, EndDate = end ?? start, WorksAsDayOfWeek = worksAs, GroupId = group, SubjectId = subject, Title = "t" };

    [Fact]
    public void Oddiy_yil_hafta_kunlari_togri_hisoblanadi()
    {
        // 2026-yil sentabr: 1-sentabr seshanba.
        var cal = Cal(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Tuesday), Entry(2, DayOfWeek.Thursday)]);
        var expected = new[] { 1, 3, 8, 10, 15, 17, 22, 24, 29 }.Select(d => new DateOnly(2026, 9, d)).ToArray();
        Assert.Equal(expected, res.Select(r => r.Date).ToArray());
        Assert.All(res, r => Assert.Equal(r.Date.DayOfWeek, r.ScheduleDayOfWeek));
    }

    [Fact]
    public void Kabisa_yili_29_fevral_hisobga_olinadi()
    {
        // 2028 — kabisa yili; 29.02.2028 — seshanba.
        var cal = Cal(new DateOnly(2028, 2, 20), new DateOnly(2028, 3, 5));
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Tuesday)]);
        Assert.Contains(new DateOnly(2028, 2, 29), res.Select(r => r.Date));
        Assert.Equal(new[] { new DateOnly(2028, 2, 22), new DateOnly(2028, 2, 29) }, res.Select(r => r.Date).ToArray());
    }

    [Fact]
    public void Oddiy_yilda_29_fevral_yoq()
    {
        var cal = Cal(new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 5));
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Monday)]);
        Assert.Equal(new[] { new DateOnly(2027, 2, 22), new DateOnly(2027, 3, 1) }, res.Select(r => r.Date).ToArray());
    }

    [Fact]
    public void Yil_almashishi_togri_ishlaydi()
    {
        var cal = Cal(new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 10));
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Monday), Entry(2, DayOfWeek.Friday)]);
        Assert.Equal(new[]
        {
            new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 1),
            new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 8),
        }, res.Select(r => r.Date).ToArray());
    }

    [Fact]
    public void Bayram_chiqarib_tashlanadi_va_korib_chiqishda_sabab_bilan_korsatiladi()
    {
        var cal = Cal(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 9));
        var ex = new[] { Ex(CalendarExceptionKind.Holiday, new DateOnly(2026, 10, 1)) };
        var entries = new[] { Entry(1, DayOfWeek.Thursday) };
        var res = LessonDateGenerator.Generate(cal, ex, entries);
        Assert.Equal(new[] { new DateOnly(2026, 10, 8) }, res.Select(r => r.Date).ToArray());

        var withSkipped = LessonDateGenerator.Generate(cal, ex, entries, includeSkipped: true);
        var skipped = Assert.Single(withSkipped, r => r.IsSkipped);
        Assert.Equal(new DateOnly(2026, 10, 1), skipped.Date);
        Assert.Contains("Bayram", skipped.SkipReason);
    }

    [Fact]
    public void Dam_olish_kunlari_jadvalda_bolsa_ham_dars_yaratilmaydi()
    {
        var cal = Cal(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)); // Du–Ju
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Saturday)]);
        Assert.Empty(res);
    }

    [Fact]
    public void Olti_kunlik_hafta_shanbani_oz_ichiga_oladi()
    {
        var cal = Cal(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);
        var res = LessonDateGenerator.Generate(cal, [], [Entry(1, DayOfWeek.Saturday)]);
        Assert.Equal(5, res.Count); // 3, 10, 17, 24, 31 oktabr
    }

    [Fact]
    public void Tatil_davri_toliq_chiqarib_tashlanadi()
    {
        var cal = Cal(new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 22));
        var ex = new[] { Ex(CalendarExceptionKind.Vacation, new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 10)) };
        var res = LessonDateGenerator.Generate(cal, ex, [Entry(1, DayOfWeek.Monday)]);
        Assert.Equal(new[] { new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 18) }, res.Select(r => r.Date).ToArray());
    }

    [Fact]
    public void Kochirilgan_ish_kuni_boshqa_kun_jadvali_boyicha_ishlaydi()
    {
        // 10.10.2026 shanba — dushanba jadvali bo'yicha ish kuni.
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11));
        var ex = new[] { Ex(CalendarExceptionKind.TransferredWorkday, new DateOnly(2026, 10, 10), worksAs: DayOfWeek.Monday) };
        var res = LessonDateGenerator.Generate(cal, ex, [Entry(1, DayOfWeek.Monday)]);
        Assert.Equal(new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 10) }, res.Select(r => r.Date).ToArray());
        Assert.Equal(DayOfWeek.Saturday, res[1].ActualDayOfWeek);
        Assert.Equal(DayOfWeek.Monday, res[1].ScheduleDayOfWeek);
    }

    [Fact]
    public void Faqat_bir_guruh_uchun_dam_olish_kunida_dars_istisnosi()
    {
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11));
        var ex = new[] { Ex(CalendarExceptionKind.TransferredWorkday, new DateOnly(2026, 10, 10), worksAs: DayOfWeek.Monday, group: 1) };
        var res = LessonDateGenerator.Generate(cal, ex, [Entry(1, DayOfWeek.Monday, group: 1), Entry(2, DayOfWeek.Monday, group: 2)]);
        Assert.Contains(res, r => r.GroupId == 1 && r.Date == new DateOnly(2026, 10, 10));
        Assert.DoesNotContain(res, r => r.GroupId == 2 && r.Date == new DateOnly(2026, 10, 10));
    }

    [Fact]
    public void Guruh_va_fanga_xos_istisno_faqat_ularga_tasir_qiladi()
    {
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));
        var ex = new[] { Ex(CalendarExceptionKind.NoLessons, new DateOnly(2026, 10, 5), group: 1, subject: 7) };
        var res = LessonDateGenerator.Generate(cal, ex,
            [Entry(1, DayOfWeek.Monday, group: 1, subject: 7), Entry(2, DayOfWeek.Monday, lessonNo: 2, group: 1, subject: 8)]);
        Assert.DoesNotContain(res, r => r.SubjectId == 7);
        Assert.Contains(res, r => r.SubjectId == 8);
    }

    [Fact]
    public void Bir_kunda_ikki_dars_tartib_raqami_boyicha()
    {
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5));
        var res = LessonDateGenerator.Generate(cal, [], [Entry(2, DayOfWeek.Monday, lessonNo: 3), Entry(1, DayOfWeek.Monday, lessonNo: 1)]);
        Assert.Equal(new[] { 1, 3 }, res.Select(r => r.LessonNumber).ToArray());
    }

    [Fact]
    public void Amal_qilish_davridan_tashqari_dars_yaratilmaydi()
    {
        var cal = Cal(new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31));
        var e = Entry(1, DayOfWeek.Monday);
        e.ValidFrom = new DateOnly(2026, 11, 1);
        var res = LessonDateGenerator.Generate(cal, [], [e]);
        Assert.All(res, r => Assert.True(r.Date >= new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Faol_bolmagan_jadval_yozuvi_hisobga_olinmaydi()
    {
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));
        var e = Entry(1, DayOfWeek.Monday);
        e.IsActive = false;
        Assert.Empty(LessonDateGenerator.Generate(cal, [], [e]));
    }

    [Fact]
    public void Notogri_kalendar_rad_etiladi()
    {
        var cal = Cal(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1));
        Assert.Throws<ArgumentException>(() => LessonDateGenerator.Generate(cal, [], []));
    }

    [Fact]
    public void Qatiy_bayramlar_oquv_yili_oraligida()
    {
        var cal = Cal(new DateOnly(2026, 9, 2), new DateOnly(2027, 6, 30));
        var list = UzbekistanHolidays.FixedHolidaysFor(cal).Select(h => h.StartDate).ToList();
        Assert.Contains(new DateOnly(2026, 10, 1), list);
        Assert.Contains(new DateOnly(2027, 3, 21), list);
        Assert.Contains(new DateOnly(2027, 5, 9), list);
        Assert.DoesNotContain(new DateOnly(2026, 9, 1), list); // o'quv yili 2-sentabrdan
        Assert.Equal(7 - 1, list.Count); // 1-sentabr tushmaydi
    }

    [Fact]
    public void Jadval_ziddiyati_oqituvchi_xona_va_guruh_boyicha()
    {
        var a = Entry(1, DayOfWeek.Monday); a.Room = "201";
        var b = Entry(0, DayOfWeek.Monday, group: 2); b.StartTime = new TimeOnly(9, 0); b.EndTime = new TimeOnly(10, 0); b.Room = "201";
        var conflicts = TimetableConflictChecker.Check(b, [a]);
        Assert.Contains(conflicts, c => c.Message.Contains("O'qituvchi"));
        Assert.Contains(conflicts, c => c.Message.Contains("xonasi"));
        Assert.DoesNotContain(conflicts, c => c.Message.Contains("Guruhda"));

        var c2 = Entry(0, DayOfWeek.Monday, group: 2); c2.TeacherUserId = 5; c2.StartTime = new TimeOnly(9, 50); c2.EndTime = new TimeOnly(11, 0);
        Assert.Empty(TimetableConflictChecker.Check(c2, [a])); // 9:50 da tugaydi — ziddiyat emas
    }
}
