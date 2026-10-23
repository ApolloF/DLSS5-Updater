using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The trust decision for the Vulkan layer folder and the fresh-temp-file writes. Never touches ProgramData or HKLM.</summary>
public sealed class VulkanLayerSecurityTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-vks-" + Guid.NewGuid().ToString("N")[..8]);

    public VulkanLayerSecurityTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private static readonly LayerAce[] Protected =
    [
        new(VulkanLayerSecurity.System, true, true),
        new(VulkanLayerSecurity.Administrators, true, true),
        new(VulkanLayerSecurity.Users, true, false),
        new(VulkanLayerSecurity.AllAppPackages, true, false),
    ];

    [Fact]
    public void AdministratorsAndSystemOwned_ProtectedAcl_IsAccepted()
    {
        Assert.Null(VulkanLayerSecurity.Refusal("x", VulkanLayerSecurity.Administrators, Protected));
        Assert.Null(VulkanLayerSecurity.Refusal("x", VulkanLayerSecurity.System, Protected));
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1001")] // a standard user
    [InlineData(VulkanLayerSecurity.Users)]
    [InlineData("S-1-3-0")] // CREATOR OWNER
    [InlineData(null)]
    public void AnyOtherOwner_IsRefused(string? owner) =>
        Assert.NotNull(VulkanLayerSecurity.Refusal("x", owner, Protected));

    [Fact]
    public void AWriteRuleForANonAdministrator_IsRefused()
    {
        LayerAce[] open = [.. Protected, new(VulkanLayerSecurity.Users, true, true)];
        Assert.NotNull(VulkanLayerSecurity.Refusal("x", VulkanLayerSecurity.Administrators, open));
        // A deny rule or a read-only rule is no reason to refuse.
        LayerAce[] fine = [.. Protected, new(VulkanLayerSecurity.Users, false, true)];
        Assert.Null(VulkanLayerSecurity.Refusal("x", VulkanLayerSecurity.Administrators, fine));
    }

    [Fact]
    public void WriteFresh_ReplacesAPlantedTempFile_InsteadOfReusingIt()
    {
        var dest = Path.Combine(_tmp, "ReShade64.json");
        var planted = dest + ".dlssu-tmp";
        File.WriteAllText(planted, "evil");
        VulkanLayerSecurity.WriteTextFresh(dest, "good");
        Assert.Equal("good", File.ReadAllText(dest));
        Assert.False(File.Exists(planted));
    }

    [Fact]
    public void WriteFresh_ReplacesAReadOnlyPlantedTemp_AndCopyMatchesSource()
    {
        var src = Path.Combine(_tmp, "src.dll");
        File.WriteAllText(src, "payload");
        var dest = Path.Combine(_tmp, "ReShade64.dll");
        File.WriteAllText(dest + ".dlssu-tmp", "evil");
        File.SetAttributes(dest + ".dlssu-tmp", FileAttributes.ReadOnly);
        VulkanLayerSecurity.CopyFresh(src, dest);
        Assert.Equal("payload", File.ReadAllText(dest));
        Assert.Equal(FileUtil.Sha256(src), FileUtil.Sha256(dest));
    }

    [Fact]
    public void WriteFresh_AFailedWrite_LeavesNoTempFile()
    {
        var dest = Path.Combine(_tmp, "x.json");
        Assert.Throws<IOException>(() => VulkanLayerSecurity.WriteFresh(dest, _ => throw new IOException("boom")));
        Assert.False(File.Exists(dest + ".dlssu-tmp"));
        Assert.False(File.Exists(dest));
    }
}
