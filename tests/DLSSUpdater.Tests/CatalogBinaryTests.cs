using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>A catalog entry gives the bytes' hash, not a path: names and hashes that could leave a cache folder are refused.</summary>
public class CatalogBinaryTests
{
    private static CatalogBinary File(string name = "tool.zip", string? sha = null) =>
        new() { Name = name, Url = "https://example.test/x", Sha256 = sha ?? new string('a', 64) };

    [Fact]
    public void A_plain_name_and_a_64_character_hex_hash_are_installable() => Assert.True(File().IsInstallable);

    [Theory]
    [InlineData(@"..\evil.dll")]
    [InlineData("../evil.dll")]
    [InlineData(@"sub\tool.zip")]
    [InlineData("..")]
    [InlineData("a|b.zip")]
    [InlineData("")]
    public void A_name_with_folders_or_invalid_characters_is_refused(string name) => Assert.False(File(name).IsInstallable);

    [Theory]
    [InlineData(@"..\..\x0123456789abcdef0123456789abcdef0123456789abcdef012345678")] // 64 chars, not hex
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("abc")]
    public void A_hash_that_is_not_64_hex_characters_is_refused(string sha) => Assert.False(File(sha: sha).IsInstallable);

    [Fact]
    public void FileName_is_only_the_last_segment() => Assert.Equal("evil.dll", File(@"..\..\evil.dll").FileName);
}
