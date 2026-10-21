using System.Text.Json.Nodes;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>
/// Install records as every released shape wrote them (fixtures follow InstallManifest.cs at the commit named) load
/// into the generic component view, and saving them again keeps every field they had.
/// </summary>
public class InstallRecordMigrationTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-mig-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _root;
    private readonly string _target;

    public InstallRecordMigrationTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "appdata");
        _root = Path.Combine(_tmp, "Game");
        _target = Path.Combine(_root, @"Game\Binaries\Win64");
        Directory.CreateDirectory(_target);
        // %TEMP% lies in %LocalAppData%: the known folders point somewhere the attacks below don't reach.
        InstallManifest.KnownFolder = tag => tag == "|localappdata|" ? Path.Combine(_tmp, "LocalAppData") : null;
    }

    public void Dispose()
    {
        InstallManifest.KnownFolder = InstallManifest.DefaultKnownFolder;
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    // 05826e8 (1.0): no Mode, no ini records.
    private const string V10 = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "dxgi.dll",
          "Opti": true,
          "ReShade": true,
          "Mfg": true,
          "DlssNr": true,
          "Dlss": true,
          "OptiTag": "v0.7.9",
          "MfgTag": "1.0",
          "DlssTag": "v310.2.1",
          "DlssNrSha": "aaaa",
          "ReShadeSha": "bbbb",
          "AntiCheatConfirmed": false,
          "Files": [
            "Game\\Binaries\\Win64\\dxgi.dll",
            "Game\\Binaries\\Win64\\OptiScaler.ini",
            "Game\\Binaries\\Win64\\ReShade64.dll",
            "Game\\Binaries\\Win64\\renodx-mfgunlock.addon64",
            "Game\\Binaries\\Win64\\nvngx_dlssnr.dll"
          ],
          "Dirs": [ "Game\\Binaries\\Win64\\OptiScaler", "Game\\Binaries\\Win64\\Licenses" ],
          "Backups": [
            { "Original": "Game\\Binaries\\Win64\\dxgi.dll", "Backup": "Game\\Binaries\\Win64\\dxgi.dll", "Kind": "file" },
            { "Original": "Engine\\Plugins\\DLSS\\nvngx_dlss.dll", "Backup": "Engine\\Plugins\\DLSS\\nvngx_dlss.dll", "Kind": "dlss", "InstalledSha": "cccc" }
          ],
          "Updated": "2026-09-24T10:00:00Z"
        }
        """;

    // 1f2bff2 (1.2): DLSS pin, Streamline, the ini values written.
    private const string V12 = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "winmm.dll",
          "Opti": true,
          "ReShade": true,
          "Mfg": false,
          "DlssNr": true,
          "Dlss": true,
          "OptiTag": "v0.8.0",
          "DlssTag": "v310.3.0",
          "DlssPinned": true,
          "DlssNrSha": "aaaa",
          "ReShadeSha": "bbbb",
          "StreamlineTag": "v2.8.0",
          "Streamline": true,
          "OptiIni": { "Plugins/LoadReshade": "true", "Menu/ShortcutKey": "0x2D" },
          "ReShadeIni": { "INPUT/KeyOverlay": "36,0,0,0" },
          "AntiCheatConfirmed": true,
          "Files": [
            "Game\\Binaries\\Win64\\winmm.dll",
            "Game\\Binaries\\Win64\\OptiScaler.ini",
            "Game\\Binaries\\Win64\\ReShade64.dll",
            "Game\\Binaries\\Win64\\ReShade.ini",
            "Game\\Binaries\\Win64\\nvngx_dlssnr.dll",
            "Game\\Binaries\\Win64\\nvngx_dlss.dll"
          ],
          "Dirs": [],
          "Backups": [
            { "Original": "Game\\Binaries\\Win64\\sl.dlss_g.dll", "Backup": "Game\\Binaries\\Win64\\sl.dlss_g.dll", "Kind": "streamline", "InstalledSha": "dddd" }
          ],
          "Updated": "2026-09-25T10:00:00Z"
        }
        """;

    // 0212c3a: ReShade-only mode.
    private const string ReShadeOnly = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "d3d12.dll",
          "Mode": "ReShadeOnly",
          "Opti": false,
          "ReShade": true,
          "Mfg": true,
          "DlssNr": false,
          "Dlss": false,
          "MfgTag": "local",
          "ReShadeSha": "bbbb",
          "Streamline": false,
          "OptiIni": {},
          "ReShadeIni": {},
          "AntiCheatConfirmed": false,
          "Files": [ "Game\\Binaries\\Win64\\d3d12.dll", "Game\\Binaries\\Win64\\renodx-mfgunlock.addon64" ],
          "Dirs": [],
          "Backups": [],
          "Updated": "2026-09-27T10:00:00Z"
        }
        """;

    // dfb0b7a: NR library ids and the RenoDX add-on by tag.
    private const string V14 = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "dxgi.dll",
          "Mode": "ReShadeOnly",
          "Opti": false,
          "ReShade": true,
          "DlssNr": true,
          "DlssNrSha": "aaaa",
          "DlssNrId": "aaaa",
          "PreviousDlssNrId": "9999",
          "RenoDx": true,
          "RenoDxTag": "snapshot",
          "ReShadeSha": "bbbb",
          "OptiIni": {},
          "ReShadeIni": { "ADDON/LoadFromDllMain": "renodx-dlss.addon64", "RENODX-DLSS/Enabled": "1" },
          "Files": [
            "Game\\Binaries\\Win64\\dxgi.dll",
            "Game\\Binaries\\Win64\\ReShade.ini",
            "Game\\Binaries\\Win64\\renodx-dlss.addon64",
            "Game\\Binaries\\Win64\\nvngx_dlssnr.dll"
          ],
          "Dirs": [],
          "Backups": [],
          "Updated": "2026-10-17T10:00:00Z"
        }
        """;

    // c2a6f1c (1.10): add-on library ids, Lecram, Uplift with its NR folder, DFC with its cfg values.
    private const string V110 = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "dxgi.dll",
          "Mode": "ReShadeOnly",
          "ReShade": true,
          "DlssNr": true,
          "DlssNrSha": "aaaa",
          "DlssNrId": "aaaa",
          "DlssNrFile": "GITC-Uplift\\nvngx_dlssnr.dll",
          "Uplift": true,
          "UpliftTag": "v1.2.0",
          "Dfc": false,
          "Lecram": false,
          "RenoDx": false,
          "DfcCfg": {},
          "ReShadeSha": "bbbb",
          "OptiIni": {},
          "ReShadeIni": { "Uplift/Enabled": "1", "GENERAL/NoReloadOnInit": "0" },
          "Files": [
            "Game\\Binaries\\Win64\\dxgi.dll",
            "Game\\Binaries\\Win64\\ReShade.ini",
            "Game\\Binaries\\Win64\\gitc-uplift.addon64",
            "Game\\Binaries\\Win64\\gitc-uplift-helper64.exe",
            "Game\\Binaries\\Win64\\reshade-shaders\\Shaders\\Uplift.fx",
            "Game\\Binaries\\Win64\\reshade-shaders\\Shaders\\UpliftMask.fx",
            "Game\\Binaries\\Win64\\GITC-Uplift\\nvngx_dlssnr.dll"
          ],
          "Dirs": [],
          "Backups": [],
          "Updated": "2026-10-18T10:00:00Z"
        }
        """;

    // c2a6f1c (1.10) with Lecram next to ShortFuse.
    private const string V110Lecram = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "dxgi.dll",
          "Mode": "ReShadeOnly",
          "ReShade": true,
          "DlssNr": true,
          "DlssNrSha": "aaaa",
          "DlssNrId": "aaaa",
          "RenoDx": true,
          "RenoDxTag": "snapshot",
          "RenoDxId": "1111",
          "Lecram": true,
          "LecramTag": "v11.2",
          "LecramId": "2222",
          "LecramFile": "dlss5-nr.addon64",
          "DfcCfg": {},
          "ReShadeSha": "bbbb",
          "OptiIni": {},
          "ReShadeIni": { "RenoDX.DLSS5/ForeignNr": "observe", "RENODX-DLSS-preset1/Exposure": "1.2" },
          "Files": [
            "Game\\Binaries\\Win64\\dxgi.dll",
            "Game\\Binaries\\Win64\\ReShade.ini",
            "Game\\Binaries\\Win64\\renodx-dlss.addon64",
            "Game\\Binaries\\Win64\\dlss5-nr.addon64",
            "Game\\Binaries\\Win64\\nvngx_dlssnr.dll"
          ],
          "Dirs": [],
          "Backups": [
            { "Original": "Game\\Binaries\\Win64\\renodx-dlss5.addon64", "Backup": "Game\\Binaries\\Win64\\renodx-dlss5.addon64", "Kind": "file" }
          ],
          "Updated": "2026-10-18T10:00:00Z"
        }
        """;

    // 79af0a3 (DFC runtimes), bbe629a (DFC file list) and 6f67370 (2.2.0, DFC library id).
    private const string V22 = """
        {
          "Schema": 1,
          "RootRel": "..\\..\\..",
          "Proxy": "dxgi.dll",
          "Mode": "ReShadeOnly",
          "ReShade": true,
          "DlssNr": true,
          "DlssNrSha": "aaaa",
          "DlssNrId": "aaaa",
          "Dfc": true,
          "DfcTag": "5.0.1",
          "DfcId": "3333",
          "DfcFiles": [
            "Game\\Binaries\\Win64\\deep-fried-chicken.addon64",
            "Game\\Binaries\\Win64\\deep-fried-chicken-nvngx.dll",
            "Game\\Binaries\\Win64\\deep-fried-chicken-neural\\model.bin"
          ],
          "DfcCfg": { "DFC/arm": "1" },
          "DfcRuntimes": [ "Game\\Binaries\\Win64\\nvngx_dlss.dll" ],
          "ReShadeSha": "bbbb",
          "OptiIni": {},
          "ReShadeIni": {},
          "AntiCheatConfirmed": false,
          "Files": [
            "Game\\Binaries\\Win64\\dxgi.dll",
            "Game\\Binaries\\Win64\\deep-fried-chicken.addon64",
            "Game\\Binaries\\Win64\\deep-fried-chicken-nvngx.dll",
            "Game\\Binaries\\Win64\\deep-fried-chicken-neural\\model.bin",
            "Game\\Binaries\\Win64\\reshade-shaders\\Shaders\\DFC_OutputState.addonfx",
            "Game\\Binaries\\Win64\\deep-fried-chicken.cfg",
            "Game\\Binaries\\Win64\\nvngx_dlss.dll",
            "Game\\Binaries\\Win64\\nvngx_dlssnr.dll"
          ],
          "Dirs": [],
          "Backups": [
            { "Original": "Game\\Binaries\\Win64\\dxgi.dll", "Backup": "Game\\Binaries\\Win64\\dxgi.dll", "Kind": "file" },
            { "Original": "Game\\Binaries\\Win64\\dlss5-feed.addon64", "Backup": "Game\\Binaries\\Win64\\dlss5-feed.addon64", "Kind": "file" }
          ],
          "Updated": "2026-10-20T10:00:00Z"
        }
        """;

    public static TheoryData<string, string[]> Shapes => new()
    {
        { V10, [ComponentRegistry.DlssNr, ComponentRegistry.OptiScaler, ComponentRegistry.ReShade, ComponentRegistry.MfgUnlock, ComponentRegistry.Dlss] },
        { V12, [ComponentRegistry.DlssNr, ComponentRegistry.OptiScaler, ComponentRegistry.ReShade, ComponentRegistry.Streamline, ComponentRegistry.Dlss] },
        { ReShadeOnly, [ComponentRegistry.ReShade, ComponentRegistry.MfgUnlock] },
        { V14, [ComponentRegistry.DlssNr, ComponentRegistry.ReShade, ComponentRegistry.RenoDx] },
        { V110, [ComponentRegistry.DlssNr, ComponentRegistry.ReShade, ComponentRegistry.Uplift] },
        { V110Lecram, [ComponentRegistry.DlssNr, ComponentRegistry.ReShade, ComponentRegistry.RenoDx, ComponentRegistry.Lecram] },
        { V22, [ComponentRegistry.Dfc, ComponentRegistry.DlssNr, ComponentRegistry.ReShade] },
    };

    private InstallManifest Load(string json)
    {
        Directory.CreateDirectory(InstallManifest.DirFor(_target));
        File.WriteAllText(InstallManifest.PathFor(_target), json);
        return InstallManifest.Read(_target)!;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void OldShape_LoadsIntoTheComponentView(string json, string[] ids)
    {
        var m = Load(json);
        var view = m.InstalledComponents();

        Assert.Equal(ids.Order(), view.Select(c => c.Id).Order());
        Assert.All(view, c => Assert.True(c.Legacy));
        Assert.Empty(m.Components);
        // Each file is claimed by one component at most.
        var claimed = view.SelectMany(c => c.Files).Select(f => f.Path).ToList();
        Assert.Equal(claimed.Count, claimed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(claimed, f => Assert.Contains(f, m.Files, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void OldShape_SavedAgain_KeepsEveryField(string json, string[] _)
    {
        var m = Load(json);
        m.Contain(_target, _root);
        m.Save(_target);

        var before = JsonNode.Parse(json)!.AsObject();
        var after = JsonNode.Parse(File.ReadAllText(InstallManifest.PathFor(_target)))!.AsObject();
        foreach (var (name, value) in before)
        {
            if (name is "Updated" or "Schema") continue;
            // Nulls are not written; missing and null mean the same.
            if (value is null) { Assert.True(after[name] is null, name); continue; }
            Assert.True(JsonNode.DeepEquals(value, after[name]), $"{name}: {value.ToJsonString()} became {after[name]?.ToJsonString()}");
        }
        Assert.Equal(InstallManifest.CurrentSchema, (int)after["Schema"]!);
        // No generic records were made up for it.
        Assert.Empty(after["Components"]!.AsArray());
    }

    [Fact]
    public void V10_AttributesFilesBackupsAndKnownHashes()
    {
        var m = Load(V10);
        var view = m.InstalledComponents().ToDictionary(c => c.Id);

        var opti = view[ComponentRegistry.OptiScaler];
        Assert.Equal("v0.7.9", opti.BuildId);
        Assert.Equal([@"Game\Binaries\Win64\dxgi.dll", @"Game\Binaries\Win64\OptiScaler.ini"], opti.Files.Select(f => f.Path));
        Assert.Equal(@"Game\Binaries\Win64\dxgi.dll", Assert.Single(opti.Backups).Key);
        Assert.Equal("dxgi.dll", opti.Extras["Proxy"]);

        Assert.Equal("bbbb", Assert.Single(view[ComponentRegistry.ReShade].Files).Sha256);
        Assert.Equal("aaaa", Assert.Single(view[ComponentRegistry.DlssNr].Files).Sha256);
        Assert.Equal("1.0", view[ComponentRegistry.MfgUnlock].BuildId);
        // The swapped DLSS file is not ours to delete, but its original is DLSS's.
        var dlss = view[ComponentRegistry.Dlss];
        Assert.Empty(dlss.Files);
        Assert.Equal(@"Engine\Plugins\DLSS\nvngx_dlss.dll", Assert.Single(dlss.Backups).Key);
    }

    [Fact]
    public void V12_IniValuesBecomeConfigEditsWithUnknownPrevious()
    {
        var m = Load(V12);
        var view = m.InstalledComponents().ToDictionary(c => c.Id);

        var edits = view[ComponentRegistry.OptiScaler].ConfigEdits;
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.True(e.PreviousUnknown));
        Assert.Contains(edits, e => e.Section == "Menu" && e.Key == "ShortcutKey" && e.Value == "0x2D" && e.File == @"Game\Binaries\Win64\OptiScaler.ini");
        Assert.Equal("true", view[ComponentRegistry.Dlss].Extras["Pinned"]);
        Assert.Equal(@"Game\Binaries\Win64\nvngx_dlss.dll", Assert.Single(view[ComponentRegistry.Dlss].Files).Path);
        var sl = view[ComponentRegistry.Streamline];
        Assert.Equal("v2.8.0", sl.BuildId);
        Assert.Equal(@"Game\Binaries\Win64\sl.dlss_g.dll", Assert.Single(sl.Backups).Key);
    }

    [Fact]
    public void ReShadeOnly_TheProxyIsReShades()
    {
        var m = Load(ReShadeOnly);
        var reshade = m.InstalledComponents().Single(c => c.Id == ComponentRegistry.ReShade);

        Assert.Equal(@"Game\Binaries\Win64\d3d12.dll", Assert.Single(reshade.Files).Path);
        Assert.Equal("bbbb", reshade.Files[0].Sha256);
        Assert.Equal("d3d12.dll", reshade.Extras["Proxy"]);
    }

    [Fact]
    public void V110_UpliftKeepsItsNrPlacementAndIniSection()
    {
        var m = Load(V110);
        var view = m.InstalledComponents().ToDictionary(c => c.Id);

        Assert.Equal(@"Game\Binaries\Win64\GITC-Uplift\nvngx_dlssnr.dll", Assert.Single(view[ComponentRegistry.DlssNr].Files).Path);
        Assert.Equal(4, view[ComponentRegistry.Uplift].Files.Count);
        Assert.Equal("Uplift", Assert.Single(view[ComponentRegistry.Uplift].ConfigEdits).Section);
        Assert.Equal("GENERAL", Assert.Single(view[ComponentRegistry.ReShade].ConfigEdits).Section);
    }

    [Fact]
    public void V110Lecram_BuildIdsAndSectionsPerAddon()
    {
        var m = Load(V110Lecram);
        var view = m.InstalledComponents().ToDictionary(c => c.Id);

        Assert.Equal("2222", view[ComponentRegistry.Lecram].BuildId);
        Assert.Equal("dlss5-nr.addon64", view[ComponentRegistry.Lecram].Extras["File"]);
        Assert.Equal("ForeignNr", Assert.Single(view[ComponentRegistry.Lecram].ConfigEdits).Key);
        Assert.Equal("1111", view[ComponentRegistry.RenoDx].BuildId);
        Assert.Equal("Exposure", Assert.Single(view[ComponentRegistry.RenoDx].ConfigEdits).Key);
        // The displaced v10 file is a backup of nothing we own now: it stays record-level, restored by uninstall.
        Assert.DoesNotContain(view.Values, c => c.Backups.Count > 0);
    }

    [Fact]
    public void V22_DfcClaimsItsSetCfgAndRuntimes()
    {
        var m = Load(V22);
        var dfc = m.InstalledComponents().Single(c => c.Id == ComponentRegistry.Dfc);

        Assert.Equal("3333", dfc.BuildId);
        Assert.Equal("5.0.1", dfc.Version);
        Assert.Equal(6, dfc.Files.Count);
        Assert.Contains(dfc.Files, f => f.Path.EndsWith("nvngx_dlss.dll"));
        Assert.Contains(dfc.Files, f => f.Path.EndsWith("DFC_OutputState.addonfx"));
        Assert.Equal("arm", Assert.Single(dfc.ConfigEdits).Key);
        Assert.DoesNotContain(m.InstalledComponents(), c => c.Id == ComponentRegistry.Dlss);
    }

    [Fact]
    public void BeforeLibraryIds_BuildIdFallsBackToTheTag()
    {
        var m = Load(V14);
        Assert.Equal("snapshot", m.InstalledComponents().Single(c => c.Id == ComponentRegistry.RenoDx).BuildId);
    }

    [Fact]
    public void NewerRecord_KeepsUnknownFieldsAndItsSchema()
    {
        var json = """
            {
              "Schema": 7,
              "RootRel": "..\\..\\..",
              "FromTheFuture": { "a": 1 },
              "Files": [ "Game\\Binaries\\Win64\\x.addon64" ],
              "Dirs": [],
              "Backups": [],
              "Components": [
                {
                  "Id": "not-registered-here",
                  "BuildId": "b1",
                  "Files": [ { "Path": "Game\\Binaries\\Win64\\x.addon64", "Sha256": "ff", "Mode": "x" } ],
                  "Shiny": true
                }
              ]
            }
            """;
        var m = Load(json);
        m.Contain(_target, _root);
        m.Save(_target);

        var after = JsonNode.Parse(File.ReadAllText(InstallManifest.PathFor(_target)))!.AsObject();
        Assert.Equal(7, (int)after["Schema"]!);
        Assert.Equal(1, (int)after["FromTheFuture"]!["a"]!);
        var c = after["Components"]![0]!;
        Assert.True((bool)c["Shiny"]!);
        Assert.Equal("x", (string)c["Files"]![0]!["Mode"]!);
        // An id this version doesn't know is kept, and shows in the view as it is.
        Assert.Equal("not-registered-here", Assert.Single(InstallManifest.Read(_target)!.InstalledComponents()).Id);
    }

    [Fact]
    public void DamagedComponentLists_AreRepairedOnRead()
    {
        var m = Load("""{ "Schema": 2, "Components": [ null, { "Id": "x", "Files": null, "Backups": null, "ConfigEdits": null, "Attributes": null, "Extras": null } ] }""");

        var c = Assert.Single(m.Components);
        Assert.Empty(c.Files);
        Assert.Empty(c.Backups);
        Assert.Empty(c.ConfigEdits);
        Assert.Empty(c.Attributes);
        Assert.Empty(c.Extras);
        Assert.False(m.IsEmpty);
    }

    [Theory]
    [InlineData("file-up")]
    [InlineData("backup-key-up")]
    [InlineData("backup-path-up")]
    [InlineData("config-outside")]
    [InlineData("config-known-folder-exe")]
    [InlineData("attribute-system")]
    [InlineData("no-id")]
    public void CraftedComponent_IsRefused(string attack)
    {
        var c = new InstalledComponent { Id = "x" };
        switch (attack)
        {
            case "file-up": c.Files.Add(new PlacedFile { Path = @"..\Victim\keep.txt" }); break;
            case "backup-key-up": c.Backups[@"..\Victim\keep.txt"] = "keep.txt"; break;
            case "backup-path-up": c.Backups[@"Game\Binaries\Win64\a.dll"] = @"..\..\..\Victim\keep.txt"; break;
            case "config-outside": c.ConfigEdits.Add(new ConfigEdit { File = Path.Combine(_tmp, @"Victim\a.ini"), Section = "s", Key = "k" }); break;
            case "config-known-folder-exe": c.ConfigEdits.Add(new ConfigEdit { File = @"|localappdata|\Victim\run.bat", Section = "s", Key = "k" }); break;
            case "attribute-system": c.Attributes.Add(new AttributeChange { Path = @"Game\Binaries\Win64\a.ini", Before = FileAttributes.System }); break;
            case "no-id": c.Id = " "; break;
        }
        var m = new InstallManifest { RootRel = @"..\..\..", Components = [c] };

        Assert.Throws<UnsafeManifestException>(() => m.Contain(_target, _root));
    }
}
