using PKForge.Switch2.Platform;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public class HostMemoryTests
{
    [Fact]
    public void FreeFraction_CountsMallocAndMonoUse() =>
        Assert.Equal(0.25, new HostMemory(HeapTotal: 1000, MallocUsed: 500, MonoUsed: 250).FreeFraction, precision: 9);

    [Fact]
    public void FreeFraction_IsZeroForAnEmptyHeap() =>
        Assert.Equal(0, new HostMemory(0, 0, 0).FreeFraction);
}
