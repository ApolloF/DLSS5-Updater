using System.Net;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>MFG Unlock 1.11.0 to 1.12.1 (docs/addons/mfg-unlock.md): version detection, the keys the app offers and writes, and the DLSS 5 rule.</summary>
public sealed class MfgConfigTests : IDisposable
{
    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private const string Digest112 = "fc5afd831c1fea5b6c0ecc7a47b0a14b37d78a0f0eecbbfba72459f14a061977";

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-mfg-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;

    public MfgConfigTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "Game");
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch (IOException) { }
    }

    private static string Release(string tag, string digest) =>
        $$"""{"tag_name":"{{tag}}","assets":[{"name":"{{ComponentStore.MfgFile}}","browser_download_url":"https://example.invalid/{{tag}}/{{ComponentStore.MfgFile}}","digest":"sha256:{{digest}}"}]}""";

    [Fact]
    public async Task NewestRelease_IsTheOneTheStoreOffers_WithItsPublishedDigest()
    {
        var body = $"[{Release("1.12.1", Digest112)},{Release("1.12.0", new string('a', 64))},{Release("1.11.0", new string('b', 64))}]";
        var gh = new FakeGitHub(r =>
        {
            var url = r.RequestUri!.ToString();
            var json = url.Contains(ComponentStore.MfgRepo + "/releases") ? body : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);

        Assert.Equal("1.12.1", store.Mfg?.Tag);
        var file = Assert.Single(store.Mfg!.Files);
        Assert.Equal(ComponentStore.MfgFile, file.Item1);
        Assert.Equal(Digest112, file.Item3);
    }

    [Theory]
    [InlineData("1.11.0", false)]
    [InlineData("1.12.0", false)]
    [InlineData("1.12.1", false)]
    [InlineData("1.12.2", true)]
    [InlineData("1.13.0", true)]
    [InlineData("local", false)]
    [InlineData(null, false)]
    public void TestedVersion_WarnsOnlyForNewerTags(string? tag, bool warns)
    {
        Assert.Equal("1.12.1", MfgConfig.TestedVersion);
        Assert.Equal(warns, MfgConfig.Warning(tag) is not null);
    }

    // The [RenoDX.MFGUnlock] table of docs/settings.md at 1.12.1 (identical at 1.11.0 and 1.12.0, and at 1.9.0 but for two legacy keys).
    private static readonly string[] Documented1121 =
    [
        "Enabled", "MaxCount", "ForceFlipMeteringOff", "TemporalFix", "BlackwellFrameworkKernels", "ExperimentalAdaptiveQuality", "AdaptiveQualityProfile",
        "AdaptiveQualityV3Photometric", "AdaptiveQualityV3DirectionalBorder", "AdaptiveQualityV3OrientedGeometry", "AdaptiveQualityV3StabilityMode",
        "AdaptiveQualityV3InpaintMode", "ThinGeometryIntermediateScatter", "ThinGeometryValidatedWarpBlend", "ThinGeometryPreviousScatter",
        "BoundaryArtifactMitigationMode", "ForceMultiplier", "DynamicMFG", "DynamicTargetFPS", "DynamicMaxMultiplier", "ReflexSourceFpsCap",
        "ReflexModeOverride", "ReflexPacingMethod", "ReflexVrrHeadroomEnabled", "ReflexVrrHeadroomBasisPoints", "DynamicReflexSourceCap",
        "FixedOutputFpsCap", "RaiseFrameCeiling", "RuntimeSelectionMode", "HDRCompatibilityMode", "DlssSuperResolutionPreset",
        "DlssRayReconstructionPreset", "DepthEdgeGuardLevel", "Rtx50QualityFixes",
    ];

    [Fact]
    public void EveryOfferedKey_IsInMfgUnlocksIniTableOf1121()
    {
        foreach (var o in SettingsOptions.Mfg([]))
        {
            Assert.Equal(MfgConfig.Section, o.Section);
            Assert.Contains(o.Key, Documented1121);
        }
    }

    [Fact]
    public void ChoiceValues_StayInsideWhatTheAddonReads()
    {
        // addon.cpp 1.12.1: out-of-range values fall back to the default, so the app never offers one.
        (int Min, int Max) Range(string key)
        {
            var v = SettingsOptions.Mfg([]).Single(o => o.Key == key).Choices.Where(c => c.Value is not null).Select(c => int.Parse(c.Value!)).ToArray();
            return (v.Min(), v.Max());
        }
        Assert.InRange(Range("DynamicTargetFPS").Max, 0, 1000);
        Assert.InRange(Range("RuntimeSelectionMode").Max, 0, 2);
        Assert.InRange(Range("HDRCompatibilityMode").Max, 0, 3);
        Assert.InRange(Range("ReflexModeOverride").Max, 0, 3);
        Assert.InRange(Range("ReflexSourceFpsCap").Min, 1, 1000);
        Assert.InRange(Range("DepthEdgeGuardLevel").Max, 0, 4);
        Assert.InRange(Range("MaxCount").Min, 2, 5);
    }

    [Fact]
    public void TheApp_OffersNoOverlayDlss5OrDiagnosticsKey()
    {
        var offered = SettingsOptions.Mfg([]).Select(o => o.Section).Distinct(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(offered, s => s.StartsWith("DLSS5", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Overlay", StringComparison.OrdinalIgnoreCase) || s.Equals("Diagnostics", StringComparison.OrdinalIgnoreCase)
            || s.Equals("DLSSG", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(HelpTopics.All.Where(t => t.Group == "MFG Unlock"), t => t.Id.Contains("dlss5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompHelp_NamesTheOverlayAndDlss5AndTheOneNeuralRoute()
    {
        var body = HelpTopics.Get("comp-mfg")!.Body;
        Assert.Contains("performance overlay", body);
        Assert.Contains("DLSS 5", body);
        Assert.Contains("never turns either on", body);
        Assert.Contains("OptiScaler", body);
        Assert.Contains("GITC Uplift", body);
        Assert.Contains("Lecram", body);
    }

    [Fact]
    public async Task Install_WritesTheChosenKeys_NeverTheAddonsOwnIniOrDlss5_AndUninstallRemovesThem()
    {
        var target = Path.Combine(_root, @"Game\Binaries\Win64");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "Game-Win64-Shipping.exe"), "EXE");
        File.WriteAllText(Path.Combine(_root, "Game.exe"), "BOOT");
        File.WriteAllText(Path.Combine(target, "sl.dlss_g.dll"), "SL-FG");
        File.WriteAllText(Path.Combine(AppPaths.Components, ComponentStore.ReShadeFile), "RESHADE");
        var tagDir = ComponentStore.TagDir(Component.MfgUnlock, "1.12.1");
        Directory.CreateDirectory(tagDir);
        File.WriteAllText(Path.Combine(tagDir, ComponentStore.MfgFile), "MFG-1.12.1");
        var release = new ReleaseInfo { Tag = "1.12.1" };
        ComponentStore.MarkComplete(Component.MfgUnlock, release);
        var store = new ComponentStore(new GitHubClient(), () => true) { Mfg = release };

        var picks = SettingsOptions.Mfg([]).Select(o => new IniOverride(o.Section, o.Key, o.Choices.Last(c => c.Value is not null).Value!)).ToList();
        var options = new InstallOptions { Mode = InstallMode.ReShadeOnly, ReShade = true, Mfg = true, Proxy = "dxgi.dll", ReShadeOverrides = picks };
        var installer = new Installer(store);
        var game = GameScanner.Inspect(new GameEntry("Game", _root, "Manual"));
        await installer.InstallAsync(game, target, options, null, default);

        Assert.Equal("MFG-1.12.1", File.ReadAllText(Path.Combine(target, ComponentStore.MfgFile)));
        Assert.Equal("1.12.1", InstallManifest.Read(target)!.MfgTag);
        var ini = IniFile.Load(Path.Combine(target, "ReShade.ini"));
        Assert.All(picks, p => Assert.Equal(p.Value, ini.Get(p.Section, p.Key)));
        Assert.False(File.Exists(Path.Combine(target, MfgConfig.AddonIni)));
        Assert.False(ini.HasSection("DLSS5"));
        Assert.Null(ini.Get("DLSS5", "Enabled"));

        await installer.UninstallAsync(game, target, default);
        Assert.False(File.Exists(Path.Combine(target, ComponentStore.MfgFile)));
        Assert.False(File.Exists(Path.Combine(target, MfgConfig.AddonIni)));
        var after = Path.Combine(target, "ReShade.ini");
        Assert.True(!File.Exists(after) || IniFile.Load(after).Get(MfgConfig.Section, "DynamicMFG") is null);
    }

    [Fact]
    public void ReleaseNotes_OfTheThreeBuilds_SummarizeToTheirFirstSection()
    {
        var overlay = ReleaseNotes.Summarize(
            "Fixes from two weeks of reports on 1.10.1, and a performance overlay.\n\n## Performance overlay\n\nAn on-screen display, off by default: **Shift+F10**.\n\n" +
            "- **Minimal:** the output frame rate.\n- **Standard** (the default when on): adds the frame time average.\n- **Full:** adds a frame time graph.\n\n## Add frame generation\n\n- **With RenoDX's DLSS addon.** Fixed.\n",
            "1.11.0");
        Assert.Equal("Performance overlay", overlay.Headline);
        Assert.StartsWith("Minimal: the output frame rate", overlay.Points[0]);
        Assert.Equal(4, overlay.Points.Count);

        var nr = ReleaseNotes.Summarize(
            "MFG Unlock can now run NVIDIA's DLSS 5 neural rendering (NR) on the game's own frames. It is off by default.\n\n## DLSS 5 tab\n\nTurn on **DLSS 5 neural rendering**, then restart the game once.\n\n" +
            "- **After the game's upscaler**, with the game's motion vectors and depth.\n- **Before Present** in any other D3D12 game.\n", "1.12.0");
        Assert.Equal("DLSS 5 tab", nr.Headline);
        Assert.Equal(2, nr.Points.Count);

        var notes = ReleaseNotes.Summarize("Fixes from the reports on 1.12.0.\n\n## DLSS 5\n\n- **The game's own motion before Present.** Uses the guides.\n- **Less ghosting.**\n", "1.12.1");
        Assert.Equal("DLSS 5", notes.Headline);
        Assert.Equal("https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag/1.12.1", ReleaseNotes.Url(Component.MfgUnlock, "1.12.1"));
    }

    [Fact]
    public void DynamicMfg_HintAndHelp_MatchThe185Acceptance()
    {
        var hint = SettingsOptions.Mfg([]).Single(o => o.Key == "DynamicMFG").Hint;
        Assert.Contains("310.9.1 or newer", hint);
        Assert.Contains("validated", hint);
        Assert.Contains("310.9.1 or newer", HelpTopics.Get("dynamic-mfg")!.Body);
    }
}
