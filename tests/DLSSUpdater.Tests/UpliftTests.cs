using System.IO.Compression;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public sealed class UpliftTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-uplift-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);
    private readonly NrVersion _nr;
    private readonly GameInfo _game;

    public UpliftTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
        _root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(_root);
        FakeVersionDll.Write(_store.ReShadePath, new Version(6, 8, 0, 0), ComponentStore.ReShadeFile);
        var nr = Path.Combine(_tmp, "nr.dll");
        FakeVersionDll.Write(nr, new Version(310, 9, 16, 0), ComponentStore.DlssNrFile);
        _nr = _store.ImportDlssNr(nr);
        var exe = G("game.exe");
        WriteExe(exe, Machine.Amd64);
        _game = new GameInfo { Id = _root, Root = _root, Name = "Uplift game", Exes = [exe], Dlss = [new DlssDll { Name = "nvngx_dlss.dll", Path = G("nvngx_dlss.dll") }] };
        var release = new ReleaseInfo { Tag = "v1.4.2" };
        var pkg = Path.Combine(ComponentStore.TagDir(Component.Uplift, release.Tag), "pkg");
        foreach (var rel in ComponentStore.UpliftFiles) FileUtil.AtomicWriteText(Path.Combine(pkg, rel), rel);
        ComponentStore.MarkComplete(Component.Uplift, release);
        _store.Uplift = release;
    }

    public void Dispose() => Directory.Delete(_tmp, true);
    private string G(string rel) => Path.Combine(_root, rel);
    private InstallOptions Options => new() { Mode = InstallMode.ReShadeOnly, Uplift = true, DlssNr = true, DlssNrId = _nr.Id };
    private IniFile Ini() => IniFile.Load(G("ReShade.ini"));
    private InstallManifest Manifest() => InstallManifest.Read(_root)!;

    private static void WriteExe(string path, Machine machine)
    {
        // A managed fixture is never launched. Only its COFF machine field is used by the installer.
        File.Copy(typeof(UpliftTests).Assembly.Location, path, true);
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        stream.Position = 0x3c;
        var offset = reader.ReadInt32();
        stream.Position = offset + 4;
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)machine);
    }

    [Theory]
    [InlineData(true, false, "4", "old")]
    [InlineData(true, true, "2", "old")]
    [InlineData(false, false, "2", null)]
    public async Task IniModes_ResetOnlyUplift_AndKeepReShadeSettings(bool carryOver, bool overwrite, string passes, string? junk)
    {
        const string original = "[Uplift]\nPassCount=4\nJunk=old\n[GENERAL]\nPresetPath=mine.ini\n[OTHER]\nSetting=keep\n[RenoDX.DLSS5]\nJunk=foreign\n";
        FileUtil.AtomicWriteText(G("ReShade.ini"), original);
        await new Installer(_store).InstallAsync(_game, _root, Options with
        {
            CarryOverIni = carryOver, OverwriteIni = overwrite, ReShadeOverrides = [new(UpliftConfig.Section, "PassCount", "2")],
        }, null, default);
        var ini = Ini();
        Assert.Equal(passes, ini.Get(UpliftConfig.Section, "PassCount"));
        Assert.Equal("1", ini.Get(UpliftConfig.Section, "Enabled"));
        Assert.Equal(junk, ini.Get(UpliftConfig.Section, "Junk"));
        Assert.Equal("mine.ini", ini.Get("GENERAL", "PresetPath"));
        Assert.Equal("keep", ini.Get("OTHER", "Setting"));
        Assert.Equal("foreign", ini.Get(LecramIni.Section, "Junk"));
        Assert.Null(DfcConfig.EffectSearchPathsFor(ini.Get("GENERAL", "EffectSearchPaths"), _root));
        var m = Manifest();
        var backup = Assert.Single(m.Backups, b => b.Original == "ReShade.ini");
        Assert.Equal(original, File.ReadAllText(Path.Combine(InstallManifest.BackupDirFor(_root), backup.Backup)));
        Assert.All(m.ReShadeIni, entry =>
        {
            var parts = entry.Key.Split('/');
            Assert.Equal(entry.Value, ini.Get(parts[0], parts[1]));
        });
    }

    [Fact]
    public async Task FreshConfig_OnRouteSwitch_RemovesOldAddonKeysFromOwnedIni()
    {
        var src = Path.Combine(_tmp, ComponentStore.RenoDxFile);
        FakeVersionDll.Write(src, new Version(0, 2026, 928, 306), ComponentStore.RenoDxFile);
        _store.ImportRenoDx(src);
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Options with
        {
            Uplift = false, ReShade = true, RenoDx = true,
            ReShadeOverrides = [new(RenoDxIni.Section, "DirectNeuralRenderingHookStage", "1")],
        }, null, default);
        Assert.True(Manifest().Owns("ReShade.ini"));
        await installer.InstallAsync(_game, _root, Options with { CarryOverIni = false }, null, default);
        var m = Manifest();
        Assert.DoesNotContain(m.Backups, b => b.Original == "ReShade.ini");
        Assert.Null(Ini().Get(RenoDxIni.Section, "DirectNeuralRenderingHookStage"));
        Assert.Equal("1", Ini().Get(UpliftConfig.Section, "Enabled"));
        Assert.DoesNotContain($"{RenoDxIni.Section}/DirectNeuralRenderingHookStage", m.ReShadeIni.Keys);
    }

    [Fact]
    public async Task Install_PlacesOnlyThe64BitSet_EnablesUplift_WritesOptionsAndTracksRuntime()
    {
        FileUtil.AtomicWriteText(G("ReShade.ini"), "[Uplift]\nEnabled=0\n[GENERAL]\nEffectSearchPaths=.\\existing\\**\n");
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Options with
        {
            ReShadeOverrides = [new("Uplift", "PassCount", "3"), new("Uplift", "Style", "2"), new("Uplift", "Enabled", "0")],
        }, null, default);
        foreach (var rel in ComponentStore.UpliftFiles) Assert.True(File.Exists(G(rel)), rel);
        Assert.False(File.Exists(G("gitc-uplift.addon32")));
        Assert.False(File.Exists(G(ComponentStore.DlssNrFile)));
        Assert.Equal(_nr.Sha256, FileUtil.Sha256(UpliftConfig.NrPath(_root)));
        Assert.Equal("1", Ini().Get("Uplift", "Enabled"));
        Assert.Equal("3", Ini().Get("Uplift", "PassCount"));
        Assert.Equal("2", Ini().Get("Uplift", "Style"));
        var paths = Ini().Get("GENERAL", "EffectSearchPaths");
        Assert.Contains("existing", paths);
        Assert.Null(DfcConfig.EffectSearchPathsFor(paths, _root));
        var m = Manifest();
        Assert.True(m.Uplift);
        Assert.True(m.ReShade);
        Assert.Equal("v1.4.2", m.UpliftTag);
        Assert.Equal(Path.Combine("GITC-Uplift", ComponentStore.DlssNrFile), m.DlssNrFile);
        Assert.All(ComponentStore.UpliftFiles.Append(m.DlssNrFile!), f => Assert.True(m.Owns(f)));
        Assert.Equal("1", m.ReShadeIni["Uplift/Enabled"]);
        // Turned off in the overlay later: a reinstall keeps the user's choice.
        var off = Ini();
        off.Set("Uplift", "Enabled", "0");
        off.Save(G("ReShade.ini"));
        await installer.InstallAsync(_game, _root, Options, null, default);
        Assert.Equal("0", Ini().Get("Uplift", "Enabled"));
        await installer.UninstallAsync(_game, _root, default);
        Assert.False(Directory.Exists(G("GITC-Uplift")));
        Assert.Equal("0", Ini().Get("Uplift", "Enabled"));
        Assert.Null(Ini().Get("Uplift", "PassCount"));
        Assert.Equal(["game.exe", "ReShade.ini"], Directory.GetFiles(_root).Select(Path.GetFileName).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Switch_FromShortFuseOrLecram_AndBack_MovesRuntimeAndRestoresOriginals(bool lecram)
    {
        var file = lecram ? ComponentStore.Lecram10File : ComponentStore.RenoDxFile;
        var src = Path.Combine(_tmp, file);
        FakeVersionDll.Write(src, new Version(0, 2026, 928, 2324), file);
        if (lecram) _store.ImportLecram(src); else _store.ImportRenoDx(src);
        var other = Options with { Uplift = false, ReShade = true, Lecram = lecram, RenoDx = !lecram };
        var installer = new Installer(_store);
        // An earlier user runtime must stay backed up while Uplift runs, including after switching repeatedly.
        File.Copy(_store.VerifiedDlssNrPath(_nr.Id), G(ComponentStore.DlssNrFile));
        await installer.InstallAsync(_game, _root, other, null, default);
        await installer.InstallAsync(_game, _root, Options, null, default);
        Assert.False(File.Exists(G(file)));
        Assert.False(File.Exists(G(ComponentStore.DlssNrFile)));
        Assert.True(File.Exists(UpliftConfig.NrPath(_root)));
        Assert.False(Manifest().Lecram);
        Assert.False(Manifest().RenoDx);
        await installer.InstallAsync(_game, _root, other, null, default);
        Assert.True(File.Exists(G(file)));
        Assert.True(File.Exists(G(ComponentStore.DlssNrFile)));
        Assert.False(Directory.Exists(G("GITC-Uplift")));
        Assert.All(ComponentStore.UpliftFiles, f => Assert.False(File.Exists(G(f))));
        Assert.Null(Ini().Get("Uplift", "Enabled"));
        Assert.False(Manifest().Uplift);
        Assert.Equal(ComponentStore.DlssNrFile, Manifest().DlssNrFile);
        await installer.InstallAsync(_game, _root, Options, null, default);
        await installer.UninstallAsync(_game, _root, default);
        Assert.True(File.Exists(G(ComponentStore.DlssNrFile)));
        Assert.False(Directory.Exists(G("GITC-Uplift")));
    }

    [Fact]
    public async Task Install_DisplacesManualRoutes_AndUninstallRestoresThem()
    {
        string[] foreign = [ComponentStore.RenoDxFile, .. ComponentStore.LecramFiles, .. ComponentStore.DfcPackageFiles, "uplift.addon64"];
        foreach (var f in foreign) FileUtil.AtomicWriteText(G(f), "original-" + f);
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Options, null, default);
        Assert.All(foreign, f => Assert.False(File.Exists(G(f))));
        await installer.UninstallAsync(_game, _root, default);
        Assert.All(foreign, f => Assert.Equal("original-" + f, File.ReadAllText(G(f))));
        Assert.False(Directory.Exists(G("GITC-Uplift")));
        Assert.False(File.Exists(G("ReShade.ini")));
    }

    [Fact]
    public async Task SwitchNr_UsesTheManifestPlacement_AndStatusReadsIt()
    {
        var src = Path.Combine(_tmp, "new-nr.dll");
        FakeVersionDll.Write(src, new Version(310, 10, 0, 0), ComponentStore.DlssNrFile);
        var newer = _store.ImportDlssNr(src);
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Options, null, default);
        await installer.SwitchDlssNrAsync(_game, _root, newer.Id, default);
        Assert.Equal(newer.Sha256, FileUtil.Sha256(UpliftConfig.NrPath(_root)));
        Assert.Equal(_nr.Id, Manifest().PreviousDlssNrId);
        Assert.False(File.Exists(G(ComponentStore.DlssNrFile)));
        _game.Installs = [_root];
        var settings = new AppSettings { DlssNrId = newer.Id };
        var vm = new GameViewModel(_game, new Services { Settings = settings, Store = _store, Installer = installer });
        Assert.Equal(RowState.Current, vm.Components.Single(c => c.Key == "dlssnr").State);
        Assert.Contains("310.10", vm.Components.Single(c => c.Key == "dlssnr").Installed);
        _store.Uplift = new ReleaseInfo { Tag = "v1.4.3" };
        vm.RefreshStatus();
        Assert.Equal(RowState.Update, vm.Components.Single(c => c.Key == "uplift").State);
        Assert.True(vm.NeedsUpdate);
        Assert.True(vm.UpdateOptions(Manifest()).Uplift);
        Assert.Equal("v1.4.2", vm.UpdateOptions(Manifest()).KeepingInstalledVersions(Manifest()).UpliftTag);
        await installer.UninstallAsync(_game, _root, default);
        Assert.Equal(["game.exe"], Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void ViewModel_UpliftAndOtherRoutesExcludeEachOther_AndModeHidesUplift()
    {
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly, InstallUplift = true };
        var vm = new GameViewModel(_game, new Services { Settings = settings, Store = _store, Installer = new Installer(_store) });
        ComponentRow Row(string key) => vm.Components.Single(c => c.Key == key);
        Assert.True(Row("uplift").Enabled);
        Assert.True(vm.BuildOptions(false, false).Uplift);
        foreach (var key in new[] { "renodx", "lecram", "dfc" })
        {
            Row(key).Enabled = true;
            Assert.False(Row("uplift").Enabled);
            Row("uplift").Enabled = true;
            Assert.False(Row(key).Enabled);
            Assert.True(Row("reshade").Enabled);
            Assert.True(Row("dlssnr").Enabled);
        }
        vm.Mode = InstallMode.OptiScaler;
        Assert.False(Row("uplift").Enabled);
        Assert.False(Row("uplift").Visible);
        Assert.False(vm.BuildOptions(false, false).Uplift);
    }

    [Fact]
    public async Task Install_RejectsConflictingRoutesAnd32BitGames_BeforeChangingGameFiles()
    {
        var installer = new Installer(_store);
        foreach (var options in new[] { Options with { Lecram = true }, Options with { RenoDx = true }, Options with { Dfc = true }, Options with { Mode = InstallMode.OptiScaler }, Options with { Opti = true }, Options with { DlssNr = false } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(_game, _root, options, null, default));
        WriteExe(_game.Exes[0], Machine.I386);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(_game, _root, Options, null, default));
        Assert.Contains("64-bit", ex.Message);
        Assert.Equal(["game.exe"], Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task Install_OptiScalerNr_DisplacesUpliftFiles_AndRestoresTheManualSet()
    {
        foreach (var rel in ComponentStore.UpliftFiles) FileUtil.AtomicWriteText(G(rel), "manual");
        var pkg = Path.Combine(ComponentStore.TagDir(Component.OptiScaler, "v9.9.9"), "pkg");
        FileUtil.AtomicWriteText(Path.Combine(pkg, "OptiScaler.dll"), "opti");
        FileUtil.AtomicWriteText(Path.Combine(pkg, "OptiScaler.ini"), "[DlssNr]\nEnabled=auto\n");
        _store.Opti = new ReleaseInfo { Tag = "v9.9.9" };
        ComponentStore.MarkComplete(Component.OptiScaler, _store.Opti);
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, new InstallOptions { Opti = true, DlssNr = true, DlssNrId = _nr.Id }, null, default);
        Assert.All(ComponentStore.UpliftFiles, f => Assert.False(File.Exists(G(f))));
        await installer.UninstallAsync(_game, _root, default);
        Assert.All(ComponentStore.UpliftFiles, f => Assert.Equal("manual", File.ReadAllText(G(f))));
        await installer.InstallAsync(_game, _root, Options, null, default);
        await installer.InstallAsync(_game, _root, new InstallOptions { Opti = true, DlssNr = true, DlssNrId = _nr.Id }, null, default);
        Assert.False(Manifest().Uplift);
        Assert.All(ComponentStore.UpliftFiles, f => Assert.False(File.Exists(G(f))));
        Assert.True(File.Exists(G(ComponentStore.DlssNrFile)));
        Assert.False(Directory.Exists(G("GITC-Uplift")));
        await installer.UninstallAsync(_game, _root, default);
        Assert.All(ComponentStore.UpliftFiles, f => Assert.Equal("manual", File.ReadAllText(G(f))));
    }

    [Fact]
    public async Task Install_WarnsForANewerTag_AndWritesAllExposedOptions()
    {
        var release = new ReleaseInfo { Tag = "v1.4.3" };
        var pkg = Path.Combine(ComponentStore.TagDir(Component.Uplift, release.Tag), "pkg");
        foreach (var rel in ComponentStore.UpliftFiles) FileUtil.AtomicWriteText(Path.Combine(pkg, rel), rel);
        ComponentStore.MarkComplete(Component.Uplift, release);
        _store.Uplift = release;
        var lines = new List<string>();
        void Capture(string line) => lines.Add(line);
        Log.Line += Capture;
        try
        {
            await new Installer(_store).InstallAsync(_game, _root, Options with
            {
                ReShadeOverrides = UpliftConfig.Defaults.Select(kv => new IniOverride("Uplift", kv.Key, kv.Value)).ToList(),
            }, null, default);
        }
        finally { Log.Line -= Capture; }
        Assert.Contains(lines, l => l.Contains("WARNING") && l.Contains("newer than the tested 1.4.2"));
        Assert.All(UpliftConfig.Defaults, kv => Assert.Equal(kv.Value, Ini().Get("Uplift", kv.Key)));
        Assert.Equal("1", Ini().Get("Uplift", "Enabled"));
    }

    [Fact]
    public async Task Install_DlssOnlyOperation_PreservesUpliftAndItsRuntime()
    {
        var installer = new Installer(_store);
        await installer.InstallAsync(_game, _root, Options, null, default);
        var release = new ReleaseInfo { Tag = "v310.9.16" };
        var cache = ComponentStore.TagDir(Component.Dlss, release.Tag);
        Directory.CreateDirectory(cache);
        FakeVersionDll.Write(Path.Combine(cache, "nvngx_dlss.dll"), new Version(310, 9, 16, 0), "nvngx_dlss.dll");
        FakeVersionDll.Write(G("nvngx_dlss.dll"), new Version(310, 8, 0, 0), "nvngx_dlss.dll");
        ComponentStore.MarkComplete(Component.Dlss, release);
        _store.Dlss = release;
        await installer.InstallAsync(_game, _root, new InstallOptions { Dlss = true }, null, default);
        Assert.Equal(new Version(310, 9, 16, 0), FileUtil.ReadVersion(G("nvngx_dlss.dll")));
        Assert.True(Manifest().Uplift);
        Assert.True(Manifest().DlssNr);
        Assert.True(File.Exists(UpliftConfig.NrPath(_root)));
        Assert.All(ComponentStore.UpliftFiles, f => Assert.True(File.Exists(G(f))));
    }

    [Theory]
    [InlineData("v1.4.2", false)]
    [InlineData("1.4.1", false)]
    [InlineData("v1.4.3", true)]
    public void TestedVersion_WarnsOnlyForNewerTags(string tag, bool warns) => Assert.Equal(warns, UpliftConfig.Warning(tag) is not null);

    [Fact]
    public void Options_ExposeExactlyTheDocumentedKeysAndDefaults()
    {
        const string expected = "EnableKey=0;Intensity=1.0;Style=0;Preset=1;PassCount=1;ResolutionMode=0;ResolutionScale=100;Source=0;PreUpscale=0;KeepFaces=0;FaceProtection=0;LightingScale=2.5;AutoMask=1;LocalStructure=1.0;LocalTone=1.0;SkinStructure=1.0;UICorrection=0;MotionVectors=0;Upsampling=1;PresentWithFrameGen=0;Encoding=0;DiffuseWhiteNits=0;InputExposure=0;AutoExposureMode=0;AutoExposureBlend=0.8;ExposureAdapt=1;VramCheck=0;GraceSeconds=5;NgxHooks=0;ForeignNr=0;VulkanNr=0;UseD3D9Ex=0;ShowNvIndicator=0;LogLevel=2;MaskBlendMode=1";
        var defaults = expected.Split(';').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1]);
        var options = SettingsOptions.Uplift([]);
        Assert.Equal(defaults.Count, options.Length);
        Assert.All(options, o =>
        {
            Assert.Equal("Uplift", o.Section);
            Assert.Equal(defaults[o.Key], o.Choices[0].Value);
            Assert.Equal(defaults[o.Key], UpliftConfig.Defaults[o.Key]);
            Assert.NotNull(HelpTopics.Get(o.Topic));
        });
        Assert.False(new AppSettings().InstallUplift);
        Assert.Contains("undocumented", options.Single(o => o.Key == "MaskBlendMode").Hint);
        Assert.Contains("310.9.16", options.Single(o => o.Key == "Preset").Hint);
        Assert.Contains("Present only", options.Single(o => o.Key == "NgxHooks").Hint);
        Assert.Contains("borderless", options.Single(o => o.Key == "UseD3D9Ex").Hint);
    }

    [Fact]
    public void Manifest_RejectsUntrustedRuntimePlacement()
    {
        var m = new InstallManifest { DlssNrFile = @"..\outside.dll" };
        Assert.Throws<UnsafeManifestException>(() => m.Contain(_root, _root));
    }

    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    [Theory]
    [InlineData("good")]
    [InlineData("bad")]
    [InlineData("missing")]
    [InlineData("no-line")]
    public async Task Download_VerifiesSums_ExtractsOnlyNeededFiles_AndWorksOffline(string sumsMode)
    {
        const string asset = "GITC-Uplift-1.4.3.zip";
        var zip = Path.Combine(_tmp, asset);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var rel in ComponentStore.UpliftFiles)
            {
                var path = rel.StartsWith(ComponentStore.DfcShaderDir) ? "Shaders/" + Path.GetFileName(rel) : "Addons/" + rel;
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write("uplift-" + rel);
            }
            using var ignored = new StreamWriter(archive.CreateEntry("Addons/GITC-Uplift/nvngx_dlssnr.dll").Open());
            ignored.Write("never extract a runtime");
        }
        var digest = FileUtil.Sha256(zip);
        var assets = new List<GhAsset> { new() { Name = asset, Url = "https://example.invalid/" + asset } };
        if (sumsMode != "missing") assets.Add(new GhAsset { Name = "SHA256SUMS.txt", Url = "https://example.invalid/SHA256SUMS.txt" });
        var release = new GhRelease { TagName = "v1.4.3", Assets = assets, Body = "Uplift release notes" };
        var body = JsonSerializer.Serialize(new List<GhRelease> { release }, JsonCtx.Default.ListGhRelease);
        var offline = false;
        var handler = new FakeGitHub(r =>
        {
            var url = r.RequestUri!.ToString();
            if (offline) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (url.Contains(ComponentStore.UpliftRepo + "/releases"))
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
                result.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"uplift\"");
                return result;
            }
            if (url.StartsWith("https://api.github.com/")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            if (url.EndsWith(asset)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(zip)) };
            if (url.EndsWith("SHA256SUMS.txt")) return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sumsMode == "no-line" ? digest + "  other.zip" : (sumsMode == "bad" ? new string('0', 64) : digest) + "  " + asset + "\n"),
            };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var store = new ComponentStore(new GitHubClient(handler), () => false);
        await store.RefreshAsync(default);
        Assert.Equal("v1.4.3", store.Uplift?.Tag);
        if (sumsMode is "bad" or "no-line")
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureUpliftAsync(null, null, default));
            Assert.False(ComponentStore.IsCached(Component.Uplift, "v1.4.3"));
            return;
        }
        var pkg = await store.EnsureUpliftAsync(null, null, default);
        Assert.True(ComponentStore.IsCached(Component.Uplift, "v1.4.3"));
        Assert.Equal(ComponentStore.UpliftFiles.Order(), Directory.EnumerateFiles(pkg, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(pkg, f)).Order());
        offline = true;
        await store.RefreshAsync(default);
        Assert.Equal("v1.4.3", store.Uplift?.Tag);
        Assert.Equal(pkg, await store.EnsureUpliftAsync(null, null, default));
        var cached = new ComponentStore(new GitHubClient(handler), () => false);
        await cached.RefreshAsync(new CancellationToken(true));
        Assert.True(cached.Uplift!.FromCache);
        Assert.Equal("Uplift release notes", cached.Uplift.Notes);
        Assert.Equal(pkg, await cached.EnsureUpliftAsync(null, null, default));
        Assert.DoesNotContain(handler.Requests, r => r.EndsWith("nvngx_dlssnr.dll"));
    }

    [Fact]
    public void Release_SelectsNewestNonDraft_IncludingPrereleases_AndOnlyTheMatchingZip()
    {
        GhRelease Release(string tag, int day, bool draft = false, bool pre = false) => new()
        {
            TagName = tag, Draft = draft, Prerelease = pre, PublishedAt = new DateTime(2026, 10, day),
            Assets = [new() { Name = $"GITC-Uplift-{tag.TrimStart('v')}.zip", Url = "https://example.invalid/package.zip" }],
        };
        var selected = ComponentStore.UpliftRelease([Release("v1.4.2", 17), Release("v1.4.3", 19, draft: true), Release("v1.4.3-beta", 18, pre: true)]);
        Assert.Equal("v1.4.3-beta", selected?.Tag);
        Assert.Equal("GITC-Uplift-1.4.3-beta.zip", selected?.Files[0].Name);
        var wrong = Release("v1.4.2", 18);
        wrong.Assets[0].Name = "source.zip";
        Assert.Null(ComponentStore.UpliftRelease([wrong]));
    }
}
