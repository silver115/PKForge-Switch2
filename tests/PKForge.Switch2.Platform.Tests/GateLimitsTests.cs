using PKForge.Switch2.Spike;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class GateLimitsTests
{
    static GateMeasurements AtLimits() => new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(3), 30, 0.25, 524288000);

    [Fact]
    public void Evaluate_PassesAtExactLimits()
    {
        var verdict = GateLimits.Evaluate(AtLimits());
        Assert.True(verdict.Passed);
        Assert.Equal(7, verdict.Rows.Count);
        Assert.All(verdict.Rows, row => Assert.True(row.Passed));
    }

    [Fact]
    public void Evaluate_FailsWhenAnyRowIsOver()
    {
        var verdict = GateLimits.Evaluate(AtLimits() with { OpenSave = TimeSpan.FromMilliseconds(5001) });
        Assert.False(verdict.Passed);
        Assert.False(verdict.Rows.Single(row => row.Measure == "Opening the test console's Legends: Z-A save").Passed);
    }

    [Fact]
    public void Evaluate_UnmeasuredRowBlocksPass()
    {
        var verdict = GateLimits.Evaluate(AtLimits() with { ColdStart = null });
        Assert.False(verdict.Passed);
        Assert.Null(verdict.Rows[0].Passed);
    }

    [Fact]
    public void Evaluate_UsesSpecWording()
    {
        var rows = GateLimits.Evaluate(AtLimits()).Rows;
        Assert.Equal("Cold start to a usable screen", rows[0].Measure);
        Assert.Equal("500 MB or less", rows[6].Limit);
    }
}
