using System.Runtime.InteropServices;
using System.Text;
using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

public sealed class NrLibraryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-nr-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentStore _store = new(new GitHubClient(), () => true);

    public NrLibraryTests()
    {
        AppPaths.Root = Path.Combine(_tmp, "data");
        AppPaths.Ensure();
    }

    public void Dispose() => Directory.Delete(_tmp, true);

    private string Dll(string name, Version version, string original = ComponentStore.DlssNrFile)
    {
        var path = Path.Combine(_tmp, name);
        FakeVersionDll.Write(path, version, original);
        Assert.Equal(version, FileUtil.ReadVersion(path));
        Assert.Equal(original, FileUtil.OriginalFilename(path));
        return path;
    }

    [Fact]
    public void VersionComesFromResource_AndImportsKeepSameVersionVariants()
    {
        var old = _store.ImportDlssNr(Dll("renamed.dll", new Version(310, 8, 0, 0)));
        var newer = _store.ImportDlssNr(Dll("new.dll", new Version(310, 9, 16, 0)));
        var future = _store.ImportDlssNr(Dll("future.dll", new Version(500, 12, 34, 56)));
        var variantPath = Dll("variant.dll", new Version(310, 9, 16, 0));
        using (var stream = File.Open(variantPath, FileMode.Append)) stream.WriteByte(42);
        var variant = _store.ImportDlssNr(variantPath);
        Assert.Equal("310.8.0", old.Version);
        Assert.Equal("310.9.16", newer.Version);
        Assert.Equal("500.12.34.56", future.Version);
        Assert.NotEqual(newer.Id, variant.Id);
        Assert.Equal(4, _store.DlssNrVersions.Count);
        Assert.All(_store.DlssNrVersions, v => Assert.True(File.Exists(_store.VerifiedDlssNrPath(v.Id))));
        Assert.Contains("Unverified", newer.Label);
        _store.ImportDlssNr(variantPath);
        Assert.Equal(4, _store.DlssNrVersions.Count);
    }

    [Fact]
    public void Import_AcceptsBuildThatNamesItselfByInternalName()
    {
        // 310.9.16 ships with OriginalFilename "CL 39025366" and InternalName "DLSSNR".
        var path = Path.Combine(_tmp, "nvngx_dlssnr.dll");
        FakeVersionDll.Write(path, new Version(310, 9, 16, 0), "CL 39025366", "DLSSNR");
        Assert.Equal("CL 39025366", FileUtil.OriginalFilename(path));
        Assert.Equal("DLSSNR", FileUtil.InternalName(path));
        Assert.Equal("310.9.16", _store.ImportDlssNr(path).Version);

        var other = Path.Combine(_tmp, "other.dll");
        FakeVersionDll.Write(other, new Version(310, 9, 16, 0), "CL 39025366", "DLSS");
        Assert.Throws<InvalidDataException>(() => _store.ImportDlssNr(other));
        Assert.Throws<InvalidDataException>(() => _store.ImportDlss(path));
    }

    [Fact]
    public void BadImports_MissingPins_TamperedFiles_AreRejected()
    {
        var text = Path.Combine(_tmp, "fake.dll");
        File.WriteAllText(text, "not a DLL");
        Assert.Null(FileUtil.ReadVersion(text));
        Assert.Throws<InvalidDataException>(() => _store.ImportDlssNr(text));
        Assert.Throws<InvalidDataException>(() => _store.ImportDlssNr(Dll("wrong.dll", new Version(1, 2, 3, 4), "other.dll")));
        Assert.Throws<InvalidDataException>(() => _store.DlssNrFor("..\\outside"));
        Assert.Throws<FileNotFoundException>(() => _store.VerifiedDlssNrPath(new string('a', 64)));
        var v = _store.ImportDlssNr(Dll("good.dll", new Version(310, 9, 16, 0)));
        File.WriteAllText(_store.DlssNrFor(v.Id), "changed");
        Assert.Throws<InvalidDataException>(() => _store.VerifiedDlssNrPath(v.Id));
    }

    [Fact]
    public void LegacyFileAndSettingsMigrate_WithoutLosingOverrides()
    {
        var legacy = Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile);
        FakeVersionDll.Write(legacy, new Version(310, 8, 0, 0), ComponentStore.DlssNrFile);
        var v = _store.MigrateDlssNr()!;
        Assert.Equal(v.Id, _store.MigrateDlssNr()!.Id);
        Assert.True(File.Exists(legacy));
        Assert.Single(_store.DlssNrVersions);
        FileUtil.AtomicWriteText(AppPaths.SettingsFile, """{"OptiSource":"wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass","DlssTag":"v310.1.0","Games":{"game":{"Proxy":"winmm.dll","DlssTag":"v3.7.0"}}}""");
        var settings = AppSettings.Load();
        Assert.Equal(ComponentStore.OptiRepo, settings.OptiSource);
        Assert.Equal("winmm.dll", settings.For("game").Proxy);
        Assert.Equal("v3.7.0", settings.For("game").DlssTag);
        Assert.Equal("v310.1.0", settings.DlssTag);
        settings.SelectDlssNr(v.Id);
        settings.SelectDlssNr(new string('b', 64));
        settings.SelectDlssNr(settings.PreviousDlssNrId!);
        settings.For("game").DlssNrId = v.Id;
        settings.Save();
        var reloaded = AppSettings.Load();
        Assert.Equal(v.Id, reloaded.DlssNrId);
        Assert.Equal(v.Id, reloaded.For("game").DlssNrId);
        Assert.Equal(new string('b', 64), reloaded.PreviousDlssNrId);
    }

    [Fact]
    public async Task SwitchAndSwitchBack_PreserveOtherFilesAndUninstallOriginal()
    {
        var root = Path.Combine(_tmp, "game");
        Directory.CreateDirectory(root);
        var game = new GameInfo { Id = root, Root = root, Name = "Fake game" };
        var destination = Path.Combine(root, ComponentStore.DlssNrFile);
        FakeVersionDll.Write(destination, new Version(300, 1, 0, 0), ComponentStore.DlssNrFile);
        var original = FileUtil.Sha256(destination);
        File.WriteAllText(Path.Combine(root, "other.txt"), "untouched");
        var old = _store.ImportDlssNr(Dll("old.dll", new Version(310, 8, 0, 0)));
        var newer = _store.ImportDlssNr(Dll("new.dll", new Version(310, 9, 16, 0)));
        var installer = new Installer(_store);
        await installer.InstallAsync(game, root, new InstallOptions { DlssNr = true, DlssNrId = old.Id }, null, default);
        await installer.SwitchDlssNrAsync(game, root, newer.Id, default);
        var m = InstallManifest.Read(root)!;
        Assert.Equal(newer.Id, m.DlssNrId);
        Assert.Equal(old.Id, m.PreviousDlssNrId);
        Assert.Equal(newer.Id, FileUtil.Sha256(destination));
        await installer.SwitchDlssNrAsync(game, root, m.PreviousDlssNrId, default);
        Assert.Equal(old.Id, FileUtil.Sha256(destination));
        Assert.Equal(newer.Id, InstallManifest.Read(root)!.PreviousDlssNrId);
        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.SwitchDlssNrAsync(game, root, new string('f', 64), default));
        Assert.Equal(old.Id, FileUtil.Sha256(destination));
        await installer.UninstallAsync(game, root, default);
        Assert.Equal(original, FileUtil.Sha256(destination));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(root, "other.txt")));
    }

    [Fact]
    public void ReShadeModeWithoutRenoDx_DoesNotRequireNrImport()
    {
        var root = Path.Combine(_tmp, "mode-game"); Directory.CreateDirectory(root);
        var settings = new AppSettings { DefaultMode = InstallMode.ReShadeOnly };
        var services = new DLSSUpdater.ViewModels.Services { Settings = settings, Store = _store, Installer = new Installer(_store) };
        var vm = new DLSSUpdater.ViewModels.GameViewModel(new GameInfo { Id = root, Root = root, Name = "Fake mode game" }, services);
        Assert.False(vm.BuildOptions(false, false).DlssNr);
        vm.Components.First(c => c.Key == "renodx").Enabled = true;
        var options = vm.BuildOptions(false, false);
        Assert.True(options.RenoDx);
        Assert.True(options.DlssNr);
        Assert.True(options.ReShade);
    }

    [Fact]
    public async Task FailedManifestSave_RollsBackTheGameFileAndRecord()
    {
        var root = Path.Combine(_tmp, "locked-game"); Directory.CreateDirectory(root);
        var game = new GameInfo { Id = root, Root = root, Name = "Fake locked game" };
        var old = _store.ImportDlssNr(Dll("old.dll", new Version(310, 8, 0, 0)));
        var newer = _store.ImportDlssNr(Dll("new.dll", new Version(310, 9, 16, 0)));
        var installer = new Installer(_store);
        await installer.InstallAsync(game, root, new InstallOptions { DlssNr = true, DlssNrId = old.Id }, null, default);
        using (var locked = new FileStream(InstallManifest.PathFor(root), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => installer.SwitchDlssNrAsync(game, root, newer.Id, default));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(old.Id, FileUtil.Sha256(Path.Combine(root, ComponentStore.DlssNrFile)));
        Assert.Equal(old.Id, InstallManifest.Read(root)!.DlssNrId);
    }

    [Fact]
    public async Task ImportedAndDownloadedDlssVersionsRemainAvailable()
    {
        var imported = _store.ImportDlss(Dll("sr.dll", new Version(310, 9, 16, 0), "nvngx_dlss.dll"));
        for (var i = 0; i < 7; i++)
        {
            var r = new ReleaseInfo { Tag = $"v310.{i}.0" };
            var dir = ComponentStore.TagDir(Component.Dlss, r.Tag);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "fake " + i);
            ComponentStore.MarkComplete(Component.Dlss, r);
        }
        Assert.Equal(8, ComponentStore.CachedReleases(Component.Dlss).Count);
        Assert.True(ComponentStore.IsCached(Component.Dlss, "v310.0.0"));
        var files = await _store.EnsureDlssAsync(imported.Tag, null, default);
        Assert.Single(files);
        Assert.Equal(new Version(310, 9, 16, 0), FileUtil.ReadVersion(files["nvngx_dlss.dll"]));
        Assert.Throws<InvalidDataException>(() => _store.ImportDlss(Dll("nr.dll", new Version(310, 9, 16, 0))));
    }
}

/// <summary>Creates inert PE fixtures with a real VERSIONINFO resource, entirely in the test's temp folder.</summary>
internal static class FakeVersionDll
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string file, bool deleteExistingResources);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr update, bool discard);

    public static void Write(string path, Version v, string original, string? internalName = null)
    {
        File.Copy(typeof(FakeVersionDll).Assembly.Location, path, true);
        var info = new uint[] { 0xFEEF04BD, 0x10000, (uint)(v.Major << 16 | v.Minor), (uint)(v.Build << 16 | v.Revision),
            (uint)(v.Major << 16 | v.Minor), (uint)(v.Build << 16 | v.Revision), 0x3f, 0, 0x40004, 2, 0, 0, 0 };
        var fixedInfo = info.SelectMany(BitConverter.GetBytes).ToArray();
        var strings = Block("StringFileInfo", [], 0, 1, Block("040904b0", [], 0, 1,
            Block("OriginalFilename", Encoding.Unicode.GetBytes(original + '\0'), (ushort)(original.Length + 1), 1),
            Block("InternalName", Encoding.Unicode.GetBytes((internalName ?? "") + '\0'), (ushort)((internalName ?? "").Length + 1), 1),
            Block("FileVersion", Encoding.Unicode.GetBytes(v + "\0"), (ushort)(v.ToString().Length + 1), 1),
            Block("ProductVersion", Encoding.Unicode.GetBytes(v + "\0"), (ushort)(v.ToString().Length + 1), 1)));
        var translation = Block("VarFileInfo", [], 0, 1, Block("Translation", [9, 4, 0xb0, 4], 4, 0));
        var resource = Block("VS_VERSION_INFO", fixedInfo, (ushort)fixedInfo.Length, 0, strings, translation);
        var update = BeginUpdateResource(path, true);
        if (update == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!UpdateResource(update, new IntPtr(16), new IntPtr(1), 0x409, resource, (uint)resource.Length))
        { EndUpdateResource(update, true); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        if (!EndUpdateResource(update, false)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static byte[] Block(string key, byte[] value, ushort valueLength, ushort type, params byte[][] children)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write((ushort)0); writer.Write(valueLength); writer.Write(type);
        writer.Write(Encoding.Unicode.GetBytes(key + '\0'));
        void Align() { while (stream.Length % 4 != 0) writer.Write((byte)0); }
        Align(); writer.Write(value);
        foreach (var child in children) { Align(); writer.Write(child); }
        stream.Position = 0; writer.Write((ushort)stream.Length);
        return stream.ToArray();
    }
}
