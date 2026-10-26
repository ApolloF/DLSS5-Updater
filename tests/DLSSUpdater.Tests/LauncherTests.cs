using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Battle.net and Rockstar discovery, on fixture registry entries and a fake titles.dat.</summary>
public class LauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dlssu-launch-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private static string Escaped(string path) => path.Replace("\\", "\\\\");

    private static Launchers.UninstallEntry Entry(string name, string? publisher, string? location) => new(name, publisher, location);

    [Fact]
    public void BattleNet_KeepsBlizzardAndActivisionGames_AndSkipsTheLauncher()
    {
        var wow = Dir("World of Warcraft");
        var cod = Dir("Call of Duty");
        var launcher = Dir("Battle.net");
        var other = Dir("Other");
        var games = Launchers.BattleNetEntries(
        [
            Entry("World of Warcraft", "Blizzard Entertainment", wow),
            Entry("Call of Duty", "Activision", cod),
            Entry("Battle.net", "Blizzard Entertainment", launcher),
            Entry("Some Tool", "Someone Else", other),
        ]);
        Assert.Equal(["World of Warcraft", "Call of Duty"], games.Select(g => g.Name));
        Assert.All(games, g => Assert.Equal("Battle.net", g.Source));
        Assert.Equal(wow, games[0].Root);
    }

    [Fact]
    public void BattleNet_AcceptsActivisionBlizzard_IgnoresCaseAndSpaces()
    {
        var dir = Dir("Diablo IV");
        var games = Launchers.BattleNetEntries([Entry("Diablo IV", " activision blizzard ", dir + "\\")]);
        Assert.Single(games);
    }

    [Fact]
    public void BattleNet_SkipsEntriesWithoutAnExistingFolder_AndTheAgent()
    {
        var agent = Dir("Agent");
        var games = Launchers.BattleNetEntries(
        [
            Entry("Gone", "Blizzard Entertainment", Path.Combine(_root, "missing")),
            Entry("No location", "Blizzard Entertainment", null),
            Entry("Blizzard Agent", "Blizzard Entertainment", agent),
        ]);
        Assert.Empty(games);
    }

    [Fact]
    public void BattleNet_ListsAFolderOnce()
    {
        var dir = Dir("Overwatch");
        var games = Launchers.BattleNetEntries(
        [
            Entry("Overwatch 2", "Blizzard Entertainment", dir),
            Entry("Overwatch 2", "Blizzard Entertainment", dir),
        ]);
        Assert.Single(games);
    }

    [Fact]
    public void Rockstar_ReadsUninstallEntries_WithoutTheLauncherAndSocialClub()
    {
        var gta = Dir("Grand Theft Auto V");
        var launcher = Dir("Launcher");
        var club = Dir("Social Club");
        var games = Launchers.RockstarEntries(
        [
            Entry("Grand Theft Auto V", "Rockstar Games", gta),
            Entry("Rockstar Games Launcher", "Rockstar Games", launcher),
            Entry("Rockstar Games Social Club", "Rockstar Games", club),
            Entry("Not Rockstar", "Someone", Dir("x")),
        ], null);
        var game = Assert.Single(games);
        Assert.Equal("Rockstar", game.Source);
        Assert.Equal(gta, game.Root);
    }

    [Fact]
    public void Rockstar_ReadsInstallFoldersFromTitlesDat()
    {
        var rdr2 = Dir("Red Dead Redemption 2");
        var max = Dir("Max Payne 3");
        var titles = Path.Combine(Dir("Launcher"), "titles.dat");
        var maxSlashed = max.Replace("\\", "/");
        var missing = Escaped(Path.Combine(_root, "missing"));
        File.WriteAllText(titles, $$"""
            { "titles": [
              { "Name": "rdr2", "InstallFolder": "{{Escaped(rdr2)}}" },
              { "Name": "mp3",  "InstallFolder" : "{{maxSlashed}}" },
              { "Name": "gone", "InstallFolder": "{{missing}}" }
            ] }
            """);
        var games = Launchers.RockstarEntries([], titles);
        Assert.Equal(["Red Dead Redemption 2", "Max Payne 3"], games.Select(g => g.Name));
        Assert.Equal(rdr2, games[0].Root);
    }

    [Fact]
    public void Rockstar_TitlesDatAndRegistry_DoNotListAGameTwice()
    {
        var gta = Dir("Grand Theft Auto V");
        var titles = Path.Combine(Dir("Launcher"), "titles.dat");
        File.WriteAllText(titles, "{ \"InstallFolder\": \"" + Escaped(gta) + "\" }");
        var games = Launchers.RockstarEntries([Entry("Grand Theft Auto V", "Rockstar Games", gta)], titles);
        Assert.Single(games);
    }

    [Fact]
    public void Rockstar_MissingOrUnreadableTitlesDat_IsIgnored()
    {
        Assert.Empty(Launchers.RockstarEntries([], Path.Combine(_root, "nope", "titles.dat")));
        var titles = Path.Combine(Dir("Launcher"), "titles.dat");
        File.WriteAllText(titles, "binary \0\0 junk");
        using var hold = new FileStream(titles, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Empty(Launchers.RockstarEntries([], titles));
    }

    [Fact]
    public void NewLaunchers_AreRankedBeforeManualEntries()
    {
        var order = GameScanner.SourcePriority;
        Assert.True(Array.IndexOf(order, "Battle.net") is >= 0 and var b && b < Array.IndexOf(order, "Manual"));
        Assert.True(Array.IndexOf(order, "Rockstar") is >= 0 and var r && r < Array.IndexOf(order, "Manual"));
    }
}
