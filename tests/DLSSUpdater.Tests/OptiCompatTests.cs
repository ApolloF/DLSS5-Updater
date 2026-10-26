using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>OptiScaler's compatibility list as the catalog carries it, looked up by title through the shared name rules.</summary>
public sealed class OptiCompatTests
{
    private const string CatalogJson = """
        {
          "schema": 1,
          "repository": "ApolloF/DLSS5-Updater-catalog",
          "sections": {
            "optiscalerCompat": {
              "updated": "2026-10-21T12:00:00Z",
              "source": "https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md",
              "hudfixSource": "https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Hudfix-incompatible.md",
              "games": [
                { "name": "Cyberpunk 2077", "page": "Cyberpunk-2077", "list": "main", "compat": "working", "inputs": ["DLSS", "FSR3", "FSR3.1/4", "XeSS"], "optiPatcher": false, "notes": "FSR4 is a preset" },
                { "name": "Hades II", "list": "main", "compat": "working", "inputs": ["FSR3.1/4"], "optiPatcher": true },
                { "name": "Hades", "list": "main", "compat": "not-working", "inputs": ["DLSS"], "optiPatcher": false, "notes": "EAC" },
                { "name": "Elden Ring", "page": "Elden-Ring-(ERSS-FG)", "list": "upscalerMods", "compat": "working", "inputs": ["FSR3.1", "XeSS"], "optiPatcher": false, "notes": "Use ERSS-FG mod" },
                { "name": "Elden Ring", "list": "main", "compat": "single-os", "inputs": ["DLSS"], "optiPatcher": false },
                { "name": "Little Nightmares", "page": "Luma-Unreal-Engine-Luma-UE", "list": "lumaUe", "compat": "working", "inputs": ["DLSS"], "optiPatcher": false }
              ],
              "hudfixIncompatible": ["Alan Wake 2", "Marvel's Spider-Man", "Marvel's Spider-Man 2", "Dying Light 2"]
            }
          }
        }
        """;

    private static OptiCompatIndex Index()
    {
        Assert.True(CatalogClient.TryParse(CatalogJson, out var doc, out _));
        return new OptiCompatIndex(doc.Sections.OptiscalerCompat);
    }

    [Fact]
    public void TheSectionIsRead_AndAnOldCatalogWithoutItIsFine()
    {
        Assert.True(CatalogClient.TryParse(CatalogJson, out var doc, out _));
        var section = doc.Sections.OptiscalerCompat!;
        Assert.Equal(6, section.Games.Count);
        Assert.Equal(["DLSS", "FSR3", "FSR3.1/4", "XeSS"], section.Games[0].Inputs);
        Assert.Equal("not-working", section.Games[2].Compat);
        Assert.Equal("lumaUe", section.Games[5].List);
        Assert.True(section.Games[1].OptiPatcher);
        Assert.Equal(4, section.HudfixIncompatible.Count);

        Assert.True(CatalogClient.TryParse("""{ "schema": 1, "sections": { } }""", out var old, out _));
        Assert.Null(old.Sections.OptiscalerCompat);
        Assert.True(new OptiCompatIndex(old.Sections.OptiscalerCompat).IsEmpty);
        Assert.Null(new OptiCompatIndex(null).Find("Cyberpunk 2077"));
    }

    [Theory]
    [InlineData("Cyberpunk 2077", "Cyberpunk 2077")]
    [InlineData("Cyberpunk 2077™", "Cyberpunk 2077")]
    [InlineData("CYBERPUNK 2077: Phantom Liberty Edition", "Cyberpunk 2077")]
    [InlineData("Hades II", "Hades II")]
    [InlineData("Hades", "Hades")]
    public void Titles_AreMatchedByTheSharedNameRules(string game, string expected)
    {
        Assert.Equal(expected, Index().Find(game)?.Name);
    }

    [Fact]
    public void ASequelIsNotItsPredecessor_AndUnknownTitlesFindNothing()
    {
        Assert.Null(Index().Find("Cyberpunk 2078"));
        Assert.Null(Index().Find("Totally Different Game"));
        Assert.Null(Index().Find(""));
    }

    [Fact]
    public void ATitleInTheMainListAndAModTable_TakesTheMainEntry()
    {
        var game = Index().Find("Elden Ring")!;

        Assert.Equal("main", game.List);
        Assert.Equal("single-os", game.Compat);
    }

    [Theory]
    [InlineData("Alan Wake 2", true)]
    [InlineData("Marvel's Spider-Man 2", true)]
    [InlineData("Marvel's Spider-Man Remastered", true)]
    [InlineData("Dying Light 2 Stay Human", true)]
    [InlineData("Dying Light", false)]
    [InlineData("Alan Wake", false)]
    [InlineData("Cyberpunk 2077", false)]
    public void HudfixIncompatibleGames_AreFoundByTitle(string game, bool expected)
    {
        Assert.Equal(expected, Index().IsHudfixIncompatible(game));
    }

    [Fact]
    public void FeedsTheAdvisor()
    {
        var index = Index();
        var game = new GameInfo { Name = "Cyberpunk 2077", Tech = new GameTech { Apis = GraphicsApis.Dx12, Bits = 64 } };

        var advice = MethodAdvisor.Advise(new AdvisorInput(game, new GpuInfo(GpuVendor.Nvidia, RtxTier.Rtx40, "RTX 4070"))
        {
            OptiCompat = index.Find(game.Name), HudfixIncompatible = index.IsHudfixIncompatible(game.Name),
        });

        Assert.Contains(6, advice.Rows);
        Assert.Equal(Method.OptiScaler, advice.Top!.Method);
        Assert.Contains("DLSS, FSR3, FSR3.1/4, XeSS", advice.Top.Reason);
    }
}
