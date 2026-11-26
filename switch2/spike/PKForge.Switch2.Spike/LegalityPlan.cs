using PKForge.Domain;

namespace PKForge.Switch2.Spike;

/// <summary>The slots the box legality timing checks: always 30, so a box with fewer Pokémon still gives a full-box time.</summary>
public static class LegalityPlan
{
    public const int BoxChecks = 30;

    /// <summary>The filled slots of the first box that has any (the party is not a box), repeated in slot order up to 30.</summary>
    public static IReadOnlyList<(int Box, int Slot)> For(IReadOnlyList<SlotSummary> slots)
    {
        var filled = slots.Where(s => s.Box >= 0 && s.Species > 0).ToList();
        if (filled.Count == 0) return [];
        var box = filled.Min(s => s.Box);
        var inBox = filled.Where(s => s.Box == box).OrderBy(s => s.Slot).Select(s => (s.Box, s.Slot)).ToList();
        return [.. Enumerable.Range(0, BoxChecks).Select(i => inBox[i % inBox.Count])];
    }
}
