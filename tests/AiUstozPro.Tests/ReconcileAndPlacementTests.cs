using AiUstozPro.Application.Calendar;
using AiUstozPro.Application.Curriculum;
using AiUstozPro.Domain;

namespace AiUstozPro.Tests;

public class ReconcileTests
{
    private static GeneratedLesson G(int entry, DateOnly d)
        => new(d, d.DayOfWeek, d.DayOfWeek, entry, 1, 1, 1, 1, new TimeOnly(8, 30), new TimeOnly(9, 50), null, 2, false, null);

    private static ExistingLesson E(int id, int entry, DateOnly d, LessonOrigin origin = LessonOrigin.Generated,
        LessonStatus status = LessonStatus.Planned, bool confirmed = false, bool att = false, DateOnly? original = null)
        => new(id, entry, d, original, origin, status, confirmed, att, false);

    private static readonly DateOnly D1 = new(2026, 10, 5), D2 = new(2026, 10, 12), D3 = new(2026, 10, 19);

    [Fact]
    public void Birinchi_hisoblashda_hammasi_qoshiladi()
    {
        var r = OccurrenceReconciler.Reconcile([], [G(1, D1), G(1, D2)]);
        Assert.Equal(2, r.ToAdd.Count);
        Assert.Empty(r.ToRemove);
    }

    [Fact]
    public void Qayta_hisoblash_takroriy_yaratmaydi()
    {
        var r = OccurrenceReconciler.Reconcile([E(10, 1, D1), E(11, 1, D2)], [G(1, D1), G(1, D2)]);
        Assert.False(r.HasChanges);
        Assert.Equal(2, r.Kept.Count);
    }

    [Fact]
    public void Yangi_bayram_himoyalanmagan_darsni_olib_tashlaydi()
    {
        var r = OccurrenceReconciler.Reconcile([E(10, 1, D1), E(11, 1, D2)], [G(1, D1)]);
        Assert.Equal(11, Assert.Single(r.ToRemove).Id);
        Assert.Empty(r.Conflicts);
    }

    [Fact]
    public void Davomat_olingan_dars_ochirilmaydi_ziddiyat_korsatiladi()
    {
        var r = OccurrenceReconciler.Reconcile([E(10, 1, D1), E(11, 1, D2, att: true)], [G(1, D1)]);
        Assert.Empty(r.ToRemove);
        var c = Assert.Single(r.Conflicts);
        Assert.Equal(11, c.Lesson.Id);
        Assert.Contains("davomat", c.Message);
    }

    [Fact]
    public void Tasdiqlangan_va_otilgan_darslar_saqlanadi()
    {
        var r = OccurrenceReconciler.Reconcile(
            [E(10, 1, D1, confirmed: true), E(11, 1, D2, status: LessonStatus.Conducted)], []);
        Assert.Empty(r.ToRemove);
        Assert.Equal(2, r.Conflicts.Count);
    }

    [Fact]
    public void Qolda_kochirilgan_dars_asl_sanasini_egallaydi_va_saqlanadi()
    {
        // D2 dagi dars D3 ga ko'chirilgan; qayta hisoblashda D2 qayta yaratilmasligi kerak.
        var moved = E(11, 1, D3, origin: LessonOrigin.Moved, original: D2);
        var r = OccurrenceReconciler.Reconcile([E(10, 1, D1), moved], [G(1, D1), G(1, D2)]);
        Assert.Empty(r.ToAdd);
        Assert.Empty(r.ToRemove);
        Assert.Contains(r.Kept, k => k.Id == 11);
    }

    [Fact]
    public void Qoshimcha_dars_hech_qachon_ochirilmaydi()
    {
        var extra = new ExistingLesson(20, null, D3, null, LessonOrigin.Manual, LessonStatus.Planned, true, false, false);
        var r = OccurrenceReconciler.Reconcile([extra], [G(1, D1)]);
        Assert.Contains(r.Kept, k => k.Id == 20);
        Assert.Single(r.ToAdd);
    }

    [Fact]
    public void Bekor_qilingan_dars_qayta_yaratilmaydi()
    {
        var r = OccurrenceReconciler.Reconcile([E(10, 1, D1, status: LessonStatus.Cancelled)], [G(1, D1)]);
        Assert.False(r.HasChanges);
    }
}

public class TopicPlacementTests
{
    private static List<LessonSlot> Lessons(int count, int hours = 2, params int[] unavailable)
        => Enumerable.Range(1, count)
            .Select(i => new LessonSlot(i, new DateOnly(2026, 9, 1).AddDays(i * 2), 1, hours, !unavailable.Contains(i)))
            .ToList();

    [Fact]
    public void Mavzular_ketma_ket_joylashtiriladi()
    {
        var r = TopicPlacer.Place(Lessons(5), [TopicSlot.Free(1, 1, 2), TopicSlot.Free(2, 2, 2), TopicSlot.Free(3, 3, 2)], TopicPlacementMode.SplitByHours);
        Assert.Equal(new[] { 1, 2, 3 }, r.Placements.Select(p => p.Parts.Single().LessonId).ToArray());
        Assert.Equal(2, r.UnusedLessonCount);
        Assert.Empty(r.NotPlaced);
    }

    [Fact]
    public void Kop_soatli_mavzu_bir_necha_darsga_bolinadi()
    {
        var r = TopicPlacer.Place(Lessons(4), [TopicSlot.Free(1, 1, 4), TopicSlot.Free(2, 2, 2)], TopicPlacementMode.SplitByHours);
        Assert.Equal(new[] { 1, 2 }, r.Placements[0].Parts.Select(p => p.LessonId).ToArray());
        Assert.Equal(4, r.Placements[0].PlacedHours);
        Assert.Equal(3, r.Placements[1].Parts.Single().LessonId);
    }

    [Fact]
    public void Bir_soatli_mavzular_bitta_darsni_bolishadi()
    {
        var r = TopicPlacer.Place(Lessons(2), [TopicSlot.Free(1, 1, 1), TopicSlot.Free(2, 2, 1), TopicSlot.Free(3, 3, 2)], TopicPlacementMode.SplitByHours);
        Assert.Equal(1, r.Placements[0].Parts.Single().LessonId);
        Assert.Equal(1, r.Placements[1].Parts.Single().LessonId);
        Assert.Equal(2, r.Placements[2].Parts.Single().LessonId);
    }

    [Fact]
    public void Har_mavzu_bitta_darsga_rejimi()
    {
        var r = TopicPlacer.Place(Lessons(3), [TopicSlot.Free(1, 1, 4), TopicSlot.Free(2, 2, 2)], TopicPlacementMode.OneLessonPerTopic);
        Assert.Equal(1, r.Placements[0].Parts.Single().LessonId);
        Assert.Equal(2, r.Placements[1].Parts.Single().LessonId);
    }

    [Fact]
    public void Bekor_qilingan_darsdan_keyin_mavzular_suriladi()
    {
        var r = TopicPlacer.Place(Lessons(4, 2, unavailable: 2), [TopicSlot.Free(1, 1, 2), TopicSlot.Free(2, 2, 2)], TopicPlacementMode.SplitByHours);
        Assert.Equal(1, r.Placements[0].Parts.Single().LessonId);
        Assert.Equal(3, r.Placements[1].Parts.Single().LessonId);
    }

    [Fact]
    public void Darslar_yetmasa_joylashmagan_mavzu_korsatiladi()
    {
        var r = TopicPlacer.Place(Lessons(2), [TopicSlot.Free(1, 1, 2), TopicSlot.Free(2, 2, 4)], TopicPlacementMode.SplitByHours);
        var np = Assert.Single(r.NotPlaced);
        Assert.Equal(2, np.Topic.Id);
        Assert.Equal(2, np.PlacedHours);
        Assert.Equal(6, r.RequiredHours);
        Assert.Equal(4, r.AvailableHours);
    }

    [Fact]
    public void Qulflangan_mavzu_oz_joyida_qoladi_keyingilari_undan_keyin()
    {
        var topics = new[]
        {
            new TopicSlot(1, 1, "a", 2, true, new[] { 3 }), // qo'lda 3-darsga qo'yilgan va o'tilgan
            TopicSlot.Free(2, 2, 2),
        };
        var r = TopicPlacer.Place(Lessons(5), topics, TopicPlacementMode.SplitByHours);
        Assert.Equal(3, r.Placements[0].Parts.Single().LessonId);
        Assert.Equal(4, r.Placements[1].Parts.Single().LessonId);
    }

    [Fact]
    public void Qayta_joylashtirishda_tasdiqlangan_mavzu_saqlanadi_farq_korsatiladi()
    {
        var lessons = Lessons(5);
        var first = TopicPlacer.Place(lessons, [TopicSlot.Free(1, 1, 2), TopicSlot.Free(2, 2, 2), TopicSlot.Free(3, 3, 2)], TopicPlacementMode.SplitByHours);
        var old = first.Placements.ToDictionary(p => p.Topic.Id, p => p.PlannedDate);

        // 1-mavzu o'tildi (qulflandi), 2-dars bekor qilindi.
        var lessons2 = Lessons(5, 2, unavailable: 2);
        var second = TopicPlacer.Place(lessons2,
            [new TopicSlot(1, 1, "a", 2, true, new[] { 1 }), TopicSlot.Free(2, 2, 2), TopicSlot.Free(3, 3, 2)],
            TopicPlacementMode.SplitByHours);
        var diff = TopicPlacer.Diff(old, second);
        Assert.DoesNotContain(diff, d => d.TopicId == 1);
        Assert.Contains(diff, d => d.TopicId == 2 && d.OldDate == lessons[1].Date && d.NewDate == lessons[2].Date);
        Assert.Contains(diff, d => d.TopicId == 3);
    }
}
