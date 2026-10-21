using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Where ReShade builds come from: the reshade.me page, the CI artifacts, the setup exe, and the checks on the DLLs.</summary>
public sealed class ReShadeSourceTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-rssrc-" + Guid.NewGuid().ToString("N")[..8]);

    public ReShadeSourceTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); }
        catch (IOException) { }
    }

    [Fact]
    public void SitePage_GivesTheNewestAddonAndPlainSetupSeparately()
    {
        var (addon, plain) = ReShadeSource.ParseSitePage(ReShadeFixtures.SitePage);
        Assert.Equal("6.8.0", addon);
        Assert.Equal("6.8.0", plain);

        (addon, plain) = ReShadeSource.ParseSitePage("downloads/ReShade_Setup_6.7.3_Addon.exe downloads/ReShade_Setup_6.10.1_Addon.exe downloads/ReShade_Setup_6.9.0.exe");
        Assert.Equal("6.10.1", addon);
        Assert.Equal("6.9.0", plain);

        Assert.Equal((null, null), ReShadeSource.ParseSitePage("<html>no links</html>"));
        Assert.Equal((null, "6.8.0"), ReShadeSource.ParseSitePage("downloads/ReShade_Setup_6.8.0.exe"));
    }

    [Fact]
    public void SetupUrls_AddOnlyTheAddonSuffix()
    {
        Assert.Equal("https://reshade.me/downloads/ReShade_Setup_6.5.1_Addon.exe", ReShadeSource.SetupUrl("6.5.1", addon: true));
        Assert.Equal("https://reshade.me/downloads/ReShade_Setup_6.5.1.exe", ReShadeSource.SetupUrl("6.5.1", addon: false));
    }

    [Fact]
    public void LegacyVersions_StartAt6_0_0_NewestFirst_WithoutOtherTags()
    {
        var tags = new[] { "v6.8.0", "v6.7.3", "v6.0.0", "v5.9.2", "v0.19.2", "v6.1.0-rc1", "nightly" };
        Assert.Equal(["6.8.0", "6.7.3", "6.0.0"], ReShadeSource.LegacyVersions(tags));
    }

    private const string RunsJson = """
        {"total_count":276,"workflow_runs":[{"id":35992797724,"head_branch":"main","head_sha":"7bf9de8b33bcc76c3177007e65d73c72dd0f34c0","event":"push",
        "created_at":"2026-09-24T11:24:08Z","artifacts_url":"https://api.github.com/repos/crosire/reshade/actions/runs/35992797724/artifacts"}]}
        """;

    private const string ArtifactsJson = """
        {"total_count":3,"artifacts":[
        {"id":1,"name":"ReShade (64-bit)","size_in_bytes":2459648,"expired":false,"digest":"sha256:982C943B7F6ABB523BFA2E22EB2188C2D2D6D0CBC58DCB898B639A42FDF35D38"},
        {"id":2,"name":"ReShade Setup","size_in_bytes":4228062,"expired":false,"digest":"sha256:475c64c7f41e008d7b9ee7af1ee229a960cb3d8e8e9a7be049596b2a65dd7b07"},
        {"id":3,"name":"ReShade (32-bit)","size_in_bytes":1975929,"expired":true,"digest":null}]}
        """;

    [Fact]
    public void Nightly_RunAndArtifactsAreParsed_WithDigestsLowercased()
    {
        var run = ReShadeSource.ParseNightlyRun(RunsJson)!;
        Assert.Equal(35992797724, run.Id);
        Assert.Equal("7bf9de8b33bcc76c3177007e65d73c72dd0f34c0", run.HeadSha);
        Assert.EndsWith("/runs/35992797724/artifacts", run.ArtifactsUrl);

        var artifacts = ReShadeSource.ParseNightlyArtifacts(ArtifactsJson);
        var a64 = artifacts.Single(a => a.Name == ReShadeSource.NightlyArtifact64);
        Assert.Equal("982c943b7f6abb523bfa2e22eb2188c2d2d6d0cbc58dcb898b639a42fdf35d38", a64.Sha256);
        Assert.False(a64.Expired);
        var a32 = artifacts.Single(a => a.Name == ReShadeSource.NightlyArtifact32);
        Assert.Null(a32.Sha256);
        Assert.True(a32.Expired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"workflow_runs":[]}""")]
    [InlineData("""{"workflow_runs":[{"id":1}]}""")]
    public void Nightly_NoUsableRun_IsNull(string json) => Assert.Null(ReShadeSource.ParseNightlyRun(json));

    [Fact]
    public void Nightly_ZipUrl_PercentEncodesTheBrackets()
    {
        // The unencoded form is a 404 on nightly.link; this is the form that was checked to download.
        Assert.Equal("https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20%2864-bit%29.zip", ReShadeSource.NightlyZipUrl("ReShade (64-bit)"));
        Assert.Equal("https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20%2832-bit%29.zip", ReShadeSource.NightlyZipUrl("ReShade (32-bit)"));
        Assert.StartsWith("https://api.github.com/repos/crosire/reshade/actions/workflows/build.yml/runs?branch=main&event=push&status=success", ReShadeSource.NightlyRunsUrl);
    }

    [Fact]
    public void Setup_ExtractsBothDllsAndNothingElse()
    {
        var folder = ReShadeFixtures.Folder(_tmp, "src", "6.8.0.1");
        var setup = Path.Combine(_tmp, "ReShade_Setup_6.8.0_Addon.exe");
        File.WriteAllBytes(setup, ReShadeFixtures.Setup(folder));
        var names = ReShadeSource.ExtractSetup(setup, Path.Combine(_tmp, "out"));
        Assert.Equal([ReShadeBuild.File64, ReShadeBuild.File32], names);
        Assert.False(File.Exists(Path.Combine(_tmp, "out", "ReShade64.json")));
        Assert.Equal(FileUtil.Sha256(Path.Combine(folder, ReShadeBuild.File64)), FileUtil.Sha256(Path.Combine(_tmp, "out", ReShadeBuild.File64)));
    }

    [Fact]
    public void Setup_AnErrorPageSavedAsTheExe_IsRefused()
    {
        var page = Path.Combine(_tmp, "ReShade_Setup.exe");
        File.WriteAllText(page, "<html>500 Internal Server Error</html>" + new string(' ', 600 * 1024));
        Assert.Throws<InvalidDataException>(() => ReShadeSource.ExtractSetup(page, Path.Combine(_tmp, "out")));
        var tiny = Path.Combine(_tmp, "tiny.exe");
        File.WriteAllBytes(tiny, [(byte)'M', (byte)'Z', 0, 0]);
        Assert.Throws<InvalidDataException>(() => ReShadeSource.ExtractSetup(tiny, Path.Combine(_tmp, "out2")));
    }

    [Fact]
    public void Zip_ExtractsTheNamedDllFromTheTopLevelOnly()
    {
        var folder = ReShadeFixtures.Folder(_tmp, "src", "6.8.1.2200");
        var zip = Path.Combine(_tmp, "nightly.zip");
        File.WriteAllBytes(zip, ReShadeFixtures.Zip(folder, ReShadeBuild.File64));
        Assert.Equal([ReShadeBuild.File64], ReShadeSource.ExtractZip(zip, Path.Combine(_tmp, "out")));

        var other = Path.Combine(_tmp, "other.zip");
        using (var a = System.IO.Compression.ZipFile.Open(other, System.IO.Compression.ZipArchiveMode.Create))
            a.CreateEntry(@"..\evil\ReShade64.dll");
        Assert.Throws<InvalidDataException>(() => ReShadeSource.ExtractZip(other, Path.Combine(_tmp, "out2")));
        Assert.False(Directory.Exists(Path.Combine(_tmp, "evil")));
    }

    [Fact]
    public void Check_AcceptsReShadeOfTheRightBitness_AndRefusesEverythingElse()
    {
        var folder = ReShadeFixtures.Folder(_tmp, "ok", "6.8.0.2155");
        var ok64 = ReShadeSource.Check(Path.Combine(folder, ReShadeBuild.File64), 64);
        Assert.True(ok64.Ok, ok64.Error);
        Assert.Equal(new Version(6, 8, 0, 2155), ok64.Version);
        Assert.True(ReShadeSource.Check(Path.Combine(folder, ReShadeBuild.File32), 32).Ok);

        // a 32-bit file called ReShade64.dll
        var wrongBits = ReShadeSource.Check(Path.Combine(folder, ReShadeBuild.File32), 64);
        Assert.False(wrongBits.Ok);
        Assert.Contains("32-bit", wrongBits.Error);

        var html = Path.Combine(_tmp, "page.dll");
        File.WriteAllText(html, "<html>not a dll</html>");
        Assert.False(ReShadeSource.Check(html, 64).Ok);

        // a real DLL of another mod
        var other = Path.Combine(_tmp, "other.dll");
        FakeVersionDll.Write(other, new Version(1, 0, 0, 0), "OptiScaler.dll", null, Path.Combine(Environment.SystemDirectory, "version.dll"), "OptiScaler", "x");
        var notReShade = ReShadeSource.Check(other, 64);
        Assert.False(notReShade.Ok);
        Assert.Contains("ReShade", notReShade.Error);

        Assert.False(ReShadeSource.Check(Path.Combine(_tmp, "missing.dll"), 64).Ok);
    }

    [Fact]
    public void VersionText_ReleasesLoseTheBuildCounter_NightliesKeepIt()
    {
        var v = new Version(6, 8, 0, 2155);
        Assert.Equal("6.8.0", ReShadeSource.VersionText(v, full: false));
        Assert.Equal("6.8.0.2155", ReShadeSource.VersionText(v, full: true));
        Assert.Equal("?", ReShadeSource.VersionText(null, full: false));
    }
}
