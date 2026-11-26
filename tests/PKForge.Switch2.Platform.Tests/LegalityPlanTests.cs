using PKForge.Domain;
using PKForge.Switch2.Spike;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class LegalityPlanTests
{
    static SlotSummary Mon(int box, int slot) => new(box, slot, 25, "Pikachu", false, true);
    static SlotSummary Empty(int box, int slot) => new(box, slot, null, null, false, false);

    [Fact]
    public void For_CyclesToThirtyChecks()
    {
        var plan = LegalityPlan.For([Mon(0, 0), Empty(0, 1), Mon(0, 4), Mon(1, 0)]);
        Assert.Equal(30, plan.Count);
        for (var i = 0; i < plan.Count; i++)
            Assert.Equal(i % 2 == 0 ? (0, 0) : (0, 4), plan[i]);
    }

    [Fact]
    public void For_UsesFirstNonEmptyBox() =>
        Assert.Equal(Enumerable.Repeat((2, 7), 30), LegalityPlan.For([Empty(0, 0), Mon(2, 7), Mon(3, 1)]));

    [Fact]
    public void For_EmptySaveGivesNoChecks() => Assert.Empty(LegalityPlan.For([Empty(0, 0)]));
}
