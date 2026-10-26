using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Full Uninstall gives the driver profile values of DXVK and the frame limiter back (FakeDrs, never the real driver).</summary>
public sealed class UninstallDriverRestoreTests
{
    private readonly FakeDrs _drs = new();
    private readonly GameOverride _over = new();

    private NvidiaGame Game => new(FakeDrs.Exe, "Game", _over);

    [Fact]
    public void DxvkPresentMethod_GoesBack()
    {
        var nvidia = new NvidiaProfile(_drs);
        var profile = _drs.AddProfile(FakeDrs.Exe);
        profile.Values[NvidiaProfileCatalog.PresentMethod] = 0;
        nvidia.SetLiliumPresentMethod(Game);
        Assert.Equal(NvidiaProfileCatalog.PresentLayeredOnDxgi, _drs.Value(NvidiaProfileCatalog.PresentMethod));

        var saved = 0;
        UninstallDriverRestore.Run(nvidia, Game, () => saved++);

        Assert.Equal(0u, _drs.Value(NvidiaProfileCatalog.PresentMethod));
        Assert.Null(_drs.Value(NvidiaProfileCatalog.PresentFlags));
        Assert.True(NvidiaRecord.Load(_over).IsEmpty);
        Assert.Equal(1, saved);
    }

    [Fact]
    public void DriverFrameLimit_GoesBack_AndItsKeyIsForgotten()
    {
        var nvidia = new NvidiaProfile(_drs);
        _drs.Define(NvidiaProfileCatalog.FrameRateLimit);
        _drs.AddProfile(FakeDrs.Exe).Values[NvidiaProfileCatalog.FrameRateLimit] = 90;
        nvidia.SetFrameRateLimit(Game, 60);
        FrameLimitSettings.SetDriverCap(_over, 60);

        UninstallDriverRestore.Run(nvidia, Game, () => { });

        Assert.Equal(90u, _drs.Value(NvidiaProfileCatalog.FrameRateLimit));
        Assert.Null(FrameLimitSettings.DriverCap(_over.PickOf(ComponentRegistry.FrameLimit)));
        Assert.True(NvidiaRecord.Load(_over).IsEmpty);
    }

    [Fact]
    public void ValuesTheAppDidNotWrite_AreLeftAlone_AndNoDriverIsFine()
    {
        var nvidia = new NvidiaProfile(_drs);
        _drs.AddProfile(FakeDrs.Exe).Values[NvidiaProfileCatalog.FrameRateLimit] = 144;
        UninstallDriverRestore.Run(nvidia, Game, () => { });
        Assert.Equal(144u, _drs.Value(NvidiaProfileCatalog.FrameRateLimit));

        var none = new FakeDrs { Available = false };
        UninstallDriverRestore.Run(new NvidiaProfile(none), Game, () => throw new InvalidOperationException("must not save"));
    }
}
