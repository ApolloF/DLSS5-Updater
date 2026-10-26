using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public class AddonPurposeTests
{
    [Fact]
    public void The_default_minimum_is_ReShade_6_0()
    {
        var purpose = new AddonPurpose("x", "too old");
        Assert.False(purpose.Allows(new Version(5, 9, 2)));
        Assert.True(purpose.Allows(new Version(6, 0, 0)));
        Assert.True(purpose.Allows(null));
    }

    [Fact]
    public void Luma_needs_6_6()
    {
        var luma = new AddonPurpose("Luma", "needs 6.6", new Version(6, 6));
        Assert.False(luma.Allows(new Version(6, 5, 1, 0)));
        Assert.True(luma.Allows(new Version(6, 6, 0)));
        Assert.True(luma.Allows(new Version(6, 8, 0, 2155)));
    }
}
