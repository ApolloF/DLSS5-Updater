using System.IO.Compression;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

public sealed class ZipStageTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dlssu-zip-" + Guid.NewGuid().ToString("N")[..8]);

    public ZipStageTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private string MakeZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_tmp, Guid.NewGuid().ToString("N")[..6] + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    [Theory]
    [InlineData("a/b.fx", "a/b.fx")]
    [InlineData(@"a\b.fx", "a/b.fx")]
    [InlineData("/abs.fx", "abs.fx")]
    [InlineData("../evil.fx", null)]
    [InlineData("a/../../evil.fx", null)]
    [InlineData(@"C:\evil.fx", null)]
    [InlineData("a/./b", null)]
    public void Entry_names_that_leave_the_folder_have_no_safe_name(string entry, string? expected) =>
        Assert.Equal(expected, ZipStage.SafeName(entry));

    [Fact]
    public void Entries_are_extracted_below_the_folder_and_a_filter_limits_them()
    {
        var zip = MakeZip(("root/Shaders/a.fx", "A"), ("root/README.md", "R"));
        var dest = Path.Combine(_tmp, "out");

        var names = ZipStage.Extract(zip, dest, n => n.StartsWith("root/Shaders/"));

        Assert.Equal(["root/Shaders/a.fx"], names);
        Assert.Equal("A", File.ReadAllText(Path.Combine(dest, @"root\Shaders\a.fx")));
        Assert.False(File.Exists(Path.Combine(dest, @"root\README.md")));
    }

    [Fact]
    public void A_zip_slip_entry_stops_the_extraction()
    {
        var zip = MakeZip(("ok.fx", "A"), ("../evil.fx", "E"));

        Assert.Throws<InvalidDataException>(() => ZipStage.Extract(zip, Path.Combine(_tmp, "out")));
        Assert.False(File.Exists(Path.Combine(_tmp, "evil.fx")));
    }
}
