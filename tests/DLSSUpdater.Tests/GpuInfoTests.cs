using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The GPU the advisor judges against: vendor and RTX generation from the adapter name. No registry, no GPU needed.</summary>
public sealed class GpuInfoTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", RtxTier.Rtx50)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", RtxTier.Rtx40)]
    [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", RtxTier.Rtx30)]
    [InlineData("NVIDIA GeForce RTX 2080", RtxTier.Rtx20)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", RtxTier.None)]
    [InlineData("NVIDIA GeForce GTX 980 Ti", RtxTier.None)]
    [InlineData("NVIDIA Quadro RTX 4000", RtxTier.Rtx20)]
    [InlineData("NVIDIA TITAN RTX", RtxTier.Rtx20)]
    [InlineData("NVIDIA RTX A4000", RtxTier.Rtx30)]
    [InlineData("NVIDIA RTX PRO 6000 Blackwell", RtxTier.Rtx50)]
    [InlineData("NVIDIA RTX 4000 Ada Generation", RtxTier.Rtx40)]
    public void NvidiaGeneration_ComesFromTheModelNumber(string name, RtxTier expected)
    {
        var gpu = GpuInfo.Parse(name, 0x10DE);

        Assert.Equal(GpuVendor.Nvidia, gpu.Vendor);
        Assert.Equal(expected, gpu.Tier);
        Assert.Equal(expected != RtxTier.None, gpu.MayRunNvidiaMethods);
    }

    [Theory]
    [InlineData("AMD Radeon RX 7900 XTX", 0x1002, GpuVendor.Amd)]
    [InlineData("AMD Radeon(TM) Graphics", 0x1002, GpuVendor.Amd)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 0x8086, GpuVendor.Intel)]
    [InlineData("Intel(R) UHD Graphics 770", null, GpuVendor.Intel)]
    [InlineData("Radeon RX 6800", null, GpuVendor.Amd)]
    [InlineData("GeForce RTX 4080", null, GpuVendor.Nvidia)]
    [InlineData("Microsoft Basic Display Adapter", null, GpuVendor.Unknown)]
    public void Vendor_ComesFromThePciIdThenTheName(string name, int? pci, GpuVendor expected)
    {
        var gpu = GpuInfo.Parse(name, pci);

        Assert.Equal(expected, gpu.Vendor);
        Assert.Equal(expected == GpuVendor.Unknown, gpu.MayRunNvidiaMethods && !gpu.IsNvidia);
    }

    [Fact]
    public void ANonNvidiaCard_NeverHasAnRtxTier_EvenWithRtxInTheName()
    {
        Assert.Equal(RtxTier.None, GpuInfo.Parse("Some RTX 4090 lookalike", 0x1002).Tier);
    }

    [Fact]
    public void Best_PrefersTheNvidiaCard_OverTheIntegratedOne_AndTheNewerGeneration()
    {
        var igpu = GpuInfo.Parse("AMD Radeon(TM) Graphics", 0x1002);
        var old = GpuInfo.Parse("GeForce RTX 2060", 0x10DE);
        var fresh = GpuInfo.Parse("GeForce RTX 4060", 0x10DE);

        Assert.Equal(fresh, GpuDetector.Best([igpu, old, fresh]));
        Assert.Equal(igpu, GpuDetector.Best([igpu, GpuInfo.Parse("Microsoft Basic Display Adapter")]));
        Assert.Equal(GpuInfo.Unknown, GpuDetector.Best([]));
    }

    [Theory]
    [InlineData(@"pci\ven_10de&dev_2684&subsys_16e610de", 0x10DE)]
    [InlineData(@"PCI\VEN_1002&DEV_744C", 0x1002)]
    [InlineData(@"root\basicdisplay", null)]
    [InlineData(null, null)]
    public void VendorId_IsReadFromTheHardwareId(string? id, int? expected)
    {
        Assert.Equal(expected, GpuDetector.VendorId(id));
    }

    [Fact]
    public void Unknown_MayRunEverything_SoDetectionTroubleNeverHidesAMethod()
    {
        Assert.True(GpuInfo.Unknown.MayRunNvidiaMethods);
        Assert.False(GpuInfo.Unknown.IsKnown);
        Assert.Equal("GPU not detected", GpuInfo.Unknown.Label);
    }

    [Fact]
    public void DetectOnThisMachine_NeverThrows()
    {
        _ = GpuDetector.Detect();
    }
}
