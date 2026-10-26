using System.Text.Json;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Per-component picks by id: the components with their own fields keep them, every other one goes in the map.</summary>
public class ComponentSettingsTests
{
    [Fact]
    public void GameOverride_MapsTheOldFieldsById()
    {
        var g = new GameOverride { OptiTag = "v1", DfcId = "d1", DlssTag = "t1" };

        Assert.Equal("v1", g.BuildPick(ComponentRegistry.OptiScaler));
        Assert.Equal("d1", g.BuildPick(ComponentRegistry.Dfc));
        Assert.Equal("t1", g.BuildPick(ComponentRegistry.Dlss));
        g.SetBuildPick(ComponentRegistry.Lecram, "l1");
        g.SetBuildPick(ComponentRegistry.Uplift, "u1");
        g.SetBuildPick(ComponentRegistry.RenoDx, "r1");
        g.SetBuildPick(ComponentRegistry.DlssNr, "n1");
        Assert.Equal(("l1", "u1", "r1", "n1"), (g.LecramId, g.UpliftTag, g.RenoDxId, g.DlssNrId));
        Assert.Null(g.Components);
    }

    [Fact]
    public void GameOverride_OtherComponentsGoInTheMap_AndAnEmptiedPickGoes()
    {
        var g = new GameOverride();
        g.SetBuildPick("renodx-hdr", "abc");

        Assert.Equal("abc", g.BuildPick("RenoDX-HDR"));
        Assert.Equal("abc", g.PickOf("renodx-hdr")!.Build);
        g.SetBuildPick("renodx-hdr", null);
        Assert.Null(g.Components);
        // Clearing a pick that was never set changes nothing.
        g.SetBuildPick("never-set", null);
        Assert.Null(g.Components);
    }

    [Fact]
    public void GameOverride_KeepsOptionsAndUnknownFieldsOfAPick()
    {
        var json = """
            {
              "Games": {
                "g": {
                  "OptiTag": "v1",
                  "Components": {
                    "renodx-hdr": { "Build": "abc", "Enabled": true, "Options": { "PeakNits": "1000" }, "FromTheFuture": 3 }
                  }
                }
              }
            }
            """;
        var s = JsonSerializer.Deserialize(json, JsonCtx.Default.AppSettings);
        var g = s!.Games["g"];

        Assert.Equal("abc", g.BuildPick("renodx-hdr"));
        Assert.True(g.PickOf("renodx-hdr")!.Enabled);
        Assert.Equal("1000", g.PickOf("renodx-hdr")!.Options!["PeakNits"]);
        g.SetBuildPick("renodx-hdr", null);
        // Options stay when only the build pick is cleared.
        Assert.Equal("1000", g.PickOf("renodx-hdr")!.Options!["PeakNits"]);

        var again = JsonSerializer.Serialize(s, JsonCtx.Default.AppSettings);
        Assert.Contains("\"FromTheFuture\": 3", again);
    }

    [Fact]
    public void AppSettings_DefaultBuildById()
    {
        var s = new AppSettings { OptiTag = "v1", DlssNrId = "n1" };

        Assert.Equal("v1", s.DefaultBuild(ComponentRegistry.OptiScaler));
        Assert.Equal("n1", s.DefaultBuild(ComponentRegistry.DlssNr));
        Assert.Null(s.DefaultBuild("renodx-hdr"));
        s.SetDefaultBuild("renodx-hdr", "abc");
        Assert.Equal("abc", s.DefaultBuild("renodx-hdr"));
        // Components with their own default keep their own setters (NR remembers the one before, for example).
        Assert.Throws<InvalidOperationException>(() => s.SetDefaultBuild(ComponentRegistry.DlssNr, "n2"));
        s.SetDefaultBuild("renodx-hdr", null);
        Assert.Null(s.Components);
    }

    [Fact]
    public void SettingsWithoutComponentPicks_SaveAsBefore()
    {
        var json = JsonSerializer.Serialize(new AppSettings { Games = { ["g"] = new GameOverride { OptiTag = "v1" } } }, JsonCtx.Default.AppSettings);

        Assert.DoesNotContain("\"Components\"", json);
    }
}
