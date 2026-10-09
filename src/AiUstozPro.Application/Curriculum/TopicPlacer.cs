using AiUstozPro.Domain;

namespace AiUstozPro.Application.Curriculum;

public sealed record LessonSlot(int Id, DateOnly Date, int LessonNumber, int Hours, bool IsAvailable = true);

public sealed record TopicSlot(int Id, int OrderNo, string Title, int Hours, bool IsLocked, IReadOnlyList<int> LockedLessonIds)
{
    public static TopicSlot Free(int id, int order, int hours, string title = "")
        => new(id, order, title, hours, false, Array.Empty<int>());
}

public sealed record PlacedPart(int LessonId, DateOnly Date, int Hours);

public sealed class TopicPlacement
{
    public required TopicSlot Topic { get; init; }
    public List<PlacedPart> Parts { get; } = new();
    public int PlacedHours => Parts.Sum(p => p.Hours);
    public bool IsFullyPlaced { get; set; }
    public DateOnly? PlannedDate => Parts.Count == 0 ? null : Parts.Min(p => p.Date);
}

public sealed class PlacementResult
{
    public List<TopicPlacement> Placements { get; } = new();
    public int AvailableLessonCount { get; set; }
    public int AvailableHours { get; set; }
    public int RequiredHours { get; set; }
    public int UnusedLessonCount { get; set; }
    public IEnumerable<TopicPlacement> NotPlaced => Placements.Where(p => !p.IsFullyPlaced);
}

public sealed record PlanChange(int TopicId, int OrderNo, string Title, DateOnly? OldDate, DateOnly? NewDate);

/// <summary>
/// KTR mavzularini dars sanalariga ketma-ket joylashtiradi.
/// Qulflangan (o'tilgan/tasdiqlangan) mavzular o'z darslarida qoladi; keyingi mavzular
/// faqat ulardan keyingi bo'sh darslarga joylashtiriladi. Bekor qilingan darslar o'tkazib yuboriladi.
/// </summary>
public static class TopicPlacer
{
    public static PlacementResult Place(IEnumerable<LessonSlot> lessons, IEnumerable<TopicSlot> topics, TopicPlacementMode mode)
    {
        var ordered = lessons.Where(l => l.IsAvailable)
                             .OrderBy(l => l.Date).ThenBy(l => l.LessonNumber).ThenBy(l => l.Id)
                             .ToList();
        var topicList = topics.OrderBy(t => t.OrderNo).ThenBy(t => t.Id).ToList();
        var lockedLessonIds = topicList.Where(t => t.IsLocked).SelectMany(t => t.LockedLessonIds).ToHashSet();
        var indexOf = ordered.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var capacity = ordered.Select(l => Math.Max(1, l.Hours)).ToArray();
        var used = new bool[ordered.Count];

        var result = new PlacementResult
        {
            AvailableLessonCount = ordered.Count,
            AvailableHours = capacity.Sum(),
            RequiredHours = topicList.Sum(t => Math.Max(1, t.Hours)),
        };

        int pointer = 0;
        foreach (var topic in topicList)
        {
            var placement = new TopicPlacement { Topic = topic };
            result.Placements.Add(placement);

            if (topic.IsLocked)
            {
                foreach (var lid in topic.LockedLessonIds)
                {
                    if (!indexOf.TryGetValue(lid, out var idx)) continue;
                    var l = ordered[idx];
                    placement.Parts.Add(new PlacedPart(l.Id, l.Date, Math.Min(capacity[idx], Math.Max(1, topic.Hours))));
                    used[idx] = true;
                    pointer = Math.Max(pointer, idx + 1);
                }
                placement.IsFullyPlaced = true; // qulflangan mavzu foydalanuvchi qarori bilan joylashgan
                continue;
            }

            int remaining = Math.Max(1, topic.Hours);
            while (remaining > 0 && pointer < ordered.Count)
            {
                if (lockedLessonIds.Contains(ordered[pointer].Id) || capacity[pointer] <= 0)
                {
                    pointer++;
                    continue;
                }
                var l = ordered[pointer];
                if (mode == TopicPlacementMode.OneLessonPerTopic)
                {
                    placement.Parts.Add(new PlacedPart(l.Id, l.Date, remaining));
                    used[pointer] = true;
                    capacity[pointer] = 0;
                    remaining = 0;
                    pointer++;
                    break;
                }
                var take = Math.Min(remaining, capacity[pointer]);
                placement.Parts.Add(new PlacedPart(l.Id, l.Date, take));
                used[pointer] = true;
                capacity[pointer] -= take;
                remaining -= take;
                if (capacity[pointer] == 0) pointer++;
            }
            placement.IsFullyPlaced = remaining == 0;
        }

        result.UnusedLessonCount = used.Count(u => !u);
        return result;
    }

    /// <summary>Avvalgi va yangi reja o'rtasidagi sana farqlari.</summary>
    public static List<PlanChange> Diff(IReadOnlyDictionary<int, DateOnly?> oldDates, PlacementResult result)
    {
        var list = new List<PlanChange>();
        foreach (var p in result.Placements)
        {
            oldDates.TryGetValue(p.Topic.Id, out var oldDate);
            var newDate = p.PlannedDate;
            if (oldDate != newDate)
                list.Add(new PlanChange(p.Topic.Id, p.Topic.OrderNo, p.Topic.Title, oldDate, newDate));
        }
        return list;
    }
}
