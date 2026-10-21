using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The Vulkan layer writer against a fake machine: no admin rights, no ProgramData, no HKLM.</summary>
public sealed class VulkanLayerTests : IDisposable
{
    /// <summary>Files and registry values in memory; the real copy is made from the build folder on disk.</summary>
    private sealed class FakeHost : IVulkanLayerHost
    {
        public string LayerDir => @"C:\ProgramData\ReShade";
        public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Copies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<(string Path, int Bits)> Registry { get; } = [];
        public bool AppPackagesAllowed { get; private set; }
        public bool CorruptCopies { get; set; }
        public bool DirectoryRemoved { get; private set; }

        public bool FileExists(string path) => Texts.ContainsKey(path) || Copies.ContainsKey(path);
        public string? ReadText(string path) => Texts.GetValueOrDefault(path);
        public void Copy(string source, string dest) => Copies[dest] = CorruptCopies ? "corrupt" : source;
        public void WriteText(string path, string text)
        {
            if (FailWriteOf is not null && path.EndsWith(FailWriteOf)) throw new IOException("disk full");
            Texts[path] = text;
        }
        public void Delete(string path) { Texts.Remove(path); Copies.Remove(path); }
        public void DeleteDirectoryIfEmpty(string dir) => DirectoryRemoved = Texts.Count == 0 && Copies.Count == 0;
        public string Sha256(string path) => Copies[path] == "corrupt" ? "00" : FileUtil.Sha256(Copies[path]);
        public void SecureLayerDir(string dir) => AppPackagesAllowed = true;
        public HashSet<string> UntrustedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? FailWriteOf { get; set; }
        public void VerifyFile(string path)
        {
            if (UntrustedFiles.Contains(path)) throw new VulkanLayerException("owned by someone else");
        }
        public void Move(string from, string to) { Copies[to] = Copies[from]; Copies.Remove(from); }
        public bool IsRegistered(string manifestPath, int bits) => Registry.Contains((manifestPath, bits));
        public void Register(string manifestPath, int bits) => Registry.Add((manifestPath, bits));
        public void Unregister(string manifestPath, int bits) => Registry.Remove((manifestPath, bits));
    }

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-vk-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeHost _host = new();
    private readonly string _build;

    public VulkanLayerTests()
    {
        _build = ReShadeFixtures.Folder(_tmp, "build", "6.8.0.2155");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    private VulkanLayerFiles Files(bool only64 = false) => new(
        Path.Combine(_build, ReShadeBuild.File64), FileUtil.Sha256(Path.Combine(_build, ReShadeBuild.File64)),
        only64 ? null : Path.Combine(_build, ReShadeBuild.File32), only64 ? null : FileUtil.Sha256(Path.Combine(_build, ReShadeBuild.File32)), "6.8.0");

    private string Dir(string name) => Path.Combine(_host.LayerDir, name);

    [Fact]
    public void Manifest_IsReShadesOwn_ForBothBitnesses()
    {
        // Same fields as crosire/reshade setup\ReShade64.json and ReShade32.json (read 2026-10-21).
        foreach (var bits in new[] { 64, 32 })
        {
            var layer = JsonNode.Parse(VulkanLayer.Manifest(bits))!["layer"]!;
            Assert.Equal("VK_LAYER_reshade", layer["name"]!.GetValue<string>());
            Assert.Equal("GLOBAL", layer["type"]!.GetValue<string>());
            Assert.Equal($".\\ReShade{bits}.dll", layer["library_path"]!.GetValue<string>());
            Assert.Equal("1.3.268", layer["api_version"]!.GetValue<string>());
            Assert.Equal("1", layer["disable_environment"]!["DISABLE_VK_LAYER_reshade_1"]!.GetValue<string>());
            Assert.Equal("VK_EXT_tooling_info", layer["device_extensions"]![0]!["name"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Install_CopiesBothDlls_WritesManifests_AndRegistersEachInItsOwnView()
    {
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
        VulkanLayer.Install(_host, Files());

        Assert.True(_host.AppPackagesAllowed);
        Assert.Contains(Dir("ReShade64.dll"), _host.Copies.Keys);
        Assert.Contains(Dir("ReShade32.dll"), _host.Copies.Keys);
        Assert.Contains(Dir("ReShade64.json"), _host.Texts.Keys);
        Assert.Contains(Dir("ReShade32.json"), _host.Texts.Keys);
        // 64-bit: HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers; 32-bit: the Wow6432Node view of it.
        Assert.Equal([(Dir("ReShade64.json"), 64), (Dir("ReShade32.json"), 32)], _host.Registry.OrderByDescending(r => r.Bits));
        var status = VulkanLayer.Status(_host);
        Assert.Equal(VulkanLayerState.Installed, status.State);
        Assert.Equal("6.8.0", status.Version);
        Assert.True(status.IsInstalled);
    }

    [Fact]
    public void Install_OnlyThe64BitDll_LeavesThe32BitLayerUnregistered()
    {
        VulkanLayer.Install(_host, Files(only64: true));
        Assert.Single(_host.Registry);
        Assert.DoesNotContain(Dir("ReShade32.dll"), _host.Copies.Keys);
        Assert.Contains("64-bit", VulkanLayer.Status(_host).Text);
        Assert.DoesNotContain("32-bit", VulkanLayer.Status(_host).Text);
    }

    [Fact]
    public void Install_ACopyThatDoesNotMatchItsBuild_LeavesNothingBehind()
    {
        _host.CorruptCopies = true;
        var ex = Assert.Throws<VulkanLayerException>(() => VulkanLayer.Install(_host, Files()));
        Assert.Contains("does not match", ex.Message);
        Assert.Empty(_host.Copies);
        Assert.Empty(_host.Texts);
        Assert.Empty(_host.Registry);
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
    }

    [Fact]
    public void Install_AgainWithAnotherBuild_ReplacesIt_AndADroppedBitnessGoes()
    {
        VulkanLayer.Install(_host, Files());
        VulkanLayer.Install(_host, Files(only64: true));
        Assert.Single(_host.Registry);
        Assert.DoesNotContain(Dir("ReShade32.json"), _host.Texts.Keys);
        Assert.Equal(VulkanLayerState.Installed, VulkanLayer.Status(_host).State);
    }

    [Fact]
    public void Install_AFileOwnedByAnotherAccount_IsRefused_AndNothingIsRegistered()
    {
        _host.UntrustedFiles.Add(Dir("ReShade64.dll.new"));
        Assert.Throws<VulkanLayerException>(() => VulkanLayer.Install(_host, Files()));
        Assert.Empty(_host.Registry);
        Assert.Empty(_host.Copies);
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
    }

    [Fact]
    public void Install_FailingOnOneBitness_KeepsTheLayerThatWasWorking()
    {
        VulkanLayer.Install(_host, Files());
        var old32 = _host.Copies[Dir("ReShade32.dll")];
        _host.FailWriteOf = "ReShade32.json";
        Assert.Throws<IOException>(() => VulkanLayer.Install(_host, Files()));

        Assert.Equal(2, _host.Registry.Count);
        Assert.Contains(Dir("ReShade32.dll"), _host.Copies.Keys);
        Assert.Equal(old32, _host.Copies[Dir("ReShade32.dll")]);
        var status = VulkanLayer.Status(_host);
        Assert.Equal(VulkanLayerState.Installed, status.State);
        Assert.Contains("64-bit and 32-bit", status.Text);
    }

    [Fact]
    public void Install_FailingOnAFreshInstall_RemovesWhatThisRunAdded()
    {
        _host.FailWriteOf = "ReShade32.json";
        Assert.Throws<IOException>(() => VulkanLayer.Install(_host, Files()));
        Assert.Empty(_host.Registry);
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
    }

    [Fact]
    public void ALayerFromReShadesOwnSetup_IsNeverReplacedOrRemoved()
    {
        _host.Registry.Add((Dir("ReShade64.json"), 64));
        _host.Texts[Dir("ReShade64.json")] = "theirs";
        var status = VulkanLayer.Status(_host);
        Assert.Equal(VulkanLayerState.InstalledByOther, status.State);
        Assert.True(status.IsInstalled);

        Assert.Equal(VulkanLayerCommand.OtherOwner, Assert.Throws<VulkanLayerException>(() => VulkanLayer.Install(_host, Files())).Code);
        Assert.Equal(VulkanLayerCommand.OtherOwner, Assert.Throws<VulkanLayerException>(() => VulkanLayer.Remove(_host)).Code);
        Assert.Equal("theirs", _host.Texts[Dir("ReShade64.json")]);
        Assert.Single(_host.Registry);
    }

    [Fact]
    public void Remove_UnregistersAndDeletesEverythingWeInstalled_AndTheFolder()
    {
        VulkanLayer.Install(_host, Files());
        VulkanLayer.Remove(_host);
        Assert.Empty(_host.Registry);
        Assert.Empty(_host.Copies);
        Assert.Empty(_host.Texts);
        Assert.True(_host.DirectoryRemoved);
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
        VulkanLayer.Remove(_host); // nothing there: fine
    }

    [Fact]
    public void AMissingFileOrRegistration_ShowsAsIncomplete_AndInstallRepairsIt()
    {
        VulkanLayer.Install(_host, Files());
        _host.Registry.Remove((Dir("ReShade32.json"), 32));
        Assert.Equal(VulkanLayerState.Incomplete, VulkanLayer.Status(_host).State);
        Assert.False(VulkanLayer.Status(_host).IsInstalled);
        VulkanLayer.Install(_host, Files());
        Assert.Equal(VulkanLayerState.Installed, VulkanLayer.Status(_host).State);
    }

    // ---- the elevated command line ----

    private int Run(params string[] args) => VulkanLayerCommand.Run([VulkanLayerCommand.Verb, .. args], _host);

    [Fact]
    public void Command_InstallThenRemove_ByExitCode()
    {
        var sha64 = FileUtil.Sha256(Path.Combine(_build, ReShadeBuild.File64));
        var sha32 = FileUtil.Sha256(Path.Combine(_build, ReShadeBuild.File32));
        Assert.Equal(VulkanLayerCommand.Ok, Run("install", _build, sha64, sha32, "6.8.0"));
        Assert.Equal(VulkanLayerState.Installed, VulkanLayer.Status(_host).State);
        Assert.Equal(VulkanLayerCommand.Ok, Run("remove"));
        Assert.Equal(VulkanLayerState.NotInstalled, VulkanLayer.Status(_host).State);
    }

    [Fact]
    public void Command_RefusesBadArgumentsWrongHashesAndFilesThatAreNotReShade()
    {
        var sha64 = FileUtil.Sha256(Path.Combine(_build, ReShadeBuild.File64));
        Assert.Equal(VulkanLayerCommand.BadArguments, VulkanLayerCommand.Run([], _host));
        Assert.Equal(VulkanLayerCommand.BadArguments, Run("install", _build));
        Assert.Equal(VulkanLayerCommand.BadArguments, Run("install", "relative\\dir", sha64, "-", "6.8.0"));
        Assert.Equal(VulkanLayerCommand.BadArguments, Run("explode"));
        Assert.Equal(VulkanLayerCommand.Refused, Run("install", _build, new string('0', 64), "-", "6.8.0"));

        var notReShade = Path.Combine(_tmp, "other");
        Directory.CreateDirectory(notReShade);
        File.WriteAllText(Path.Combine(notReShade, ReShadeBuild.File64), "not a dll");
        Assert.Equal(VulkanLayerCommand.Refused, Run("install", notReShade, FileUtil.Sha256(Path.Combine(notReShade, ReShadeBuild.File64)), "-", "1.0"));
        Assert.Empty(_host.Registry);
    }

    [Fact]
    public void Command_ArgumentsOfTheParent_RoundTripThroughTheRunner()
    {
        var build = new ReShadeBuild("id", "6.8.0", ReShadeChannel.Stable, "aa", null, ReShadeVerification.RecordedAtDownload, null, DateTime.UtcNow);
        Assert.Equal(["install", _build, "aa", "-", "6.8.0"], VulkanLayerCommand.InstallArguments(_build, build));
        Assert.Equal(["remove"], VulkanLayerCommand.RemoveArguments);
        Assert.Contains("ReShade's own setup", VulkanLayerCommand.MessageFor(VulkanLayerCommand.OtherOwner));
    }
}
