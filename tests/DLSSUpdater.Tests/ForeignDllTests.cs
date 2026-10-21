using System.Text;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Which mod owns a dll under a proxy name: ReShade, OptiScaler, DXVK, Special K, ENB, or nobody we know.</summary>
public sealed class ForeignDllTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dlssu-foreign-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly string System64 = Path.Combine(Environment.SystemDirectory, "version.dll");

    public ForeignDllTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private string WithVersion(string name, string original, string? product = null, string? company = null, string? internalName = null)
    {
        var path = Path.Combine(_dir, name);
        FakeVersionDll.Write(path, new Version(1, 2, 3, 4), original, internalName, System64, product, company);
        return path;
    }

    private string WithBytes(string name, params string[] fragments)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [.. Encoding.ASCII.GetBytes("MZ-not-a-real-pe\0"), .. fragments.SelectMany(f => Encoding.ASCII.GetBytes(f + "\0\0"))]);
        return path;
    }

    // Version resources as the real builds have them (checked on ReShade 6.8.0, DXVK 3.1.1 and Special K 26.10.10).

    [Fact]
    public void ReShade_ByItsVersionResource()
    {
        var id = ForeignDll.Identify(WithVersion("dxgi.dll", "ReShade64.dll", "ReShade", "crosire"));
        Assert.Equal(DllKind.ReShade, id.Kind);
        Assert.False(id.IsForeign);
        Assert.Null(ForeignDll.Warning("dxgi.dll", id));
    }

    [Fact]
    public void OptiScaler_ByItsOriginalFilename() =>
        Assert.Equal(DllKind.OptiScaler, ForeignDll.Identify(WithVersion("dxgi.dll", "OptiScaler.dll")).Kind);

    [Fact]
    public void Dxvk_ByItsProductName_WhateverTheFileIsCalled()
    {
        foreach (var name in new[] { "dxgi.dll", "d3d9.dll", "d3d8.dll" })
            Assert.Equal(DllKind.Dxvk, ForeignDll.Identify(WithVersion(name, name, "DXVK", "DXVK")).Kind);
    }

    [Fact]
    public void SpecialK_ByItsProductNameOrInternalName_RenamedToAProxy()
    {
        Assert.Equal(DllKind.SpecialK, ForeignDll.Identify(WithVersion("dxgi.dll", "", "Special K", "The Special☆K Group", "SpecialK64.dll")).Kind);
        Assert.Equal(DllKind.SpecialK, ForeignDll.Identify(WithVersion("d3d9.dll", "", null, null, "SpecialK32.dll")).Kind);
    }

    [Fact]
    public void Enb_ByItsProductName()
    {
        Assert.Equal(DllKind.Enb, ForeignDll.Identify(WithVersion("d3d11.dll", "", "ENBSeries", "Boris Vorontsov")).Kind);
    }

    // Without a version resource the file's own text decides.

    [Fact]
    public void Strings_SpecialK_NeedItsSymbolsAndName_NotJustTheWords()
    {
        Assert.Equal(DllKind.SpecialK, ForeignDll.Identify(WithBytes("dxgi.dll", "SK_ImGui_ControlPanel", "Special K")).Kind);
        Assert.Equal(DllKind.SpecialK, ForeignDll.Identify(WithBytes("dxgi.dll", "SpecialK64.dll")).Kind);
        Assert.Equal(DllKind.Unknown, ForeignDll.Identify(WithBytes("dxgi.dll", "Special K")).Kind);
    }

    [Fact]
    public void Strings_DxvkAndReShade()
    {
        Assert.Equal(DllKind.Dxvk, ForeignDll.Identify(WithBytes("dxgi.dll", "DXVK_CONFIG_FILE")).Kind);
        Assert.Equal(DllKind.ReShade, ForeignDll.Identify(WithBytes("dxgi.dll", "https://reshade.me", "crosire")).Kind);
        Assert.Equal(DllKind.Unknown, ForeignDll.Identify(WithBytes("dxgi.dll", "reshade.me")).Kind);
        Assert.Equal(DllKind.OptiScaler, ForeignDll.Identify(WithBytes("dxgi.dll", "OptiScaler.ini", "OptiScaler")).Kind);
    }

    [Fact]
    public void Enb_ByItsCompanionFilesOrItsStrings()
    {
        var plain = WithBytes("d3d9.dll", "some wrapper");
        Assert.Equal(DllKind.Unknown, ForeignDll.Identify(plain).Kind);
        File.WriteAllText(Path.Combine(_dir, "enblocal.ini"), "[PROXY]");
        Assert.Equal(DllKind.Enb, ForeignDll.Identify(plain).Kind);
        File.Delete(Path.Combine(_dir, "enblocal.ini"));

        Assert.Equal(DllKind.Enb, ForeignDll.Identify(WithBytes("d3d11.dll", "...ENBSeries v0.4...")).Kind);
        Assert.Equal(DllKind.Enb, ForeignDll.Identify(WithBytes("d3d11.dll", "x", "enbseries.ini")).Kind);
    }

    [Fact]
    public void AnythingElse_IsUnknown_AndANoneExistingFileIsMissing()
    {
        var unknown = ForeignDll.Identify(WithBytes("dxgi.dll", "random bytes"));
        Assert.Equal(DllKind.Unknown, unknown.Kind);
        Assert.True(unknown.IsForeign);
        Assert.Equal(DllKind.Missing, ForeignDll.Identify(Path.Combine(_dir, "nope.dll")).Kind);
        Assert.False(ForeignDll.Identify(Path.Combine(_dir, "nope.dll")).IsForeign);
    }

    [Fact]
    public void Warning_IsOneLine_NamingTheOwner_AndSayingItIsBackedUp()
    {
        var sk = ForeignDll.Warning("dxgi.dll", new DllIdentity(DllKind.SpecialK, null, ""))!;
        Assert.DoesNotContain('\n', sk);
        Assert.Contains("Special K", sk);
        Assert.Contains("backed up", sk);
        var unknown = ForeignDll.Warning("d3d9.dll", new DllIdentity(DllKind.Unknown, null, ""))!;
        Assert.Contains("recognise", unknown);
        Assert.Contains("backed up", unknown);
        Assert.Null(ForeignDll.Warning("dxgi.dll", new DllIdentity(DllKind.Missing, null, "")));
    }

    [Fact]
    public void AMinorUnreadableFile_IsUnknownNotAnError()
    {
        var path = WithBytes("dxgi.dll", "x");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(DllKind.Unknown, ForeignDll.Identify(path).Kind);
    }
}
