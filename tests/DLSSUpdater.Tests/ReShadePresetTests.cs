using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>ReShade presets: validated on import, kept in a library, applied to a game, and checked for shaders the game lacks.</summary>
public sealed class ReShadePresetTests : ExtrasTestBase
{
    private const string Valid = "PreviewSplitPos=0.5\r\nTechniques=Bloom@Bloom.fx,LiliumTM@lilium__a.fx\r\nTechniqueSorting=Bloom@Bloom.fx\r\n\r\n[Bloom.fx]\r\nStrength=1\r\n";

    private string Preset(string name, string text = Valid)
    {
        var path = Path.Combine(Tmp, "incoming", name + ".ini");
        Write(path, text);
        return path;
    }

    [Theory]
    [InlineData("Techniques=A@a.fx", true)]
    [InlineData("Techniques=A@a.fx,B@Sub.FX", true)]
    [InlineData("[General]\r\nTechniques=A@a.fx", false)]
    [InlineData("Techniques=", false)]
    [InlineData("Techniques=NoAt,AlsoNot", false)]
    [InlineData("Techniques=A@a.fxh", false)]
    [InlineData("PreviewSplitPos=0.5", false)]
    [InlineData("", false)]
    public void Only_a_root_Techniques_line_with_Name_at_File_fx_is_a_preset(string text, bool valid)
    {
        var check = ReShadePresets.Check(text);

        Assert.Equal(valid, check.Valid);
        Assert.Equal(valid, check.Problem is null);
    }

    [Fact]
    public void The_needed_effect_files_are_listed_once_in_lower_case()
    {
        var check = ReShadePresets.Check("Techniques=A@Bloom.fx,B@bloom.FX,C@Other.fx");

        Assert.Equal(["bloom.fx", "other.fx"], check.Effects);
        Assert.Equal(3, check.Techniques.Count);
    }

    [Fact]
    public void A_binary_or_huge_file_is_not_imported()
    {
        var bin = Path.Combine(Tmp, "incoming", "bin.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
        File.WriteAllBytes(bin, [0x54, 0, 0x65, 0]);
        Assert.Throws<InvalidDataException>(() => ReShadePresets.Import(bin));

        var big = Path.Combine(Tmp, "incoming", "big.ini");
        File.WriteAllText(big, "Techniques=A@a.fx\r\n" + new string('x', (int)ReShadePresets.MaxBytes));
        Assert.Throws<InvalidDataException>(() => ReShadePresets.Import(big));
        Assert.Empty(ReShadePresets.List());
    }

    [Fact]
    public void Importing_stores_a_copy_and_lists_it_and_a_second_import_replaces_it()
    {
        var name = ReShadePresets.Import(Preset("My Look"));

        Assert.Equal("My Look", name);
        Assert.Equal(Valid, File.ReadAllText(ReShadePresets.PathOf(name)));
        var info = Assert.Single(ReShadePresets.List());
        Assert.True(info.Check.Valid);
        ReShadePresets.Import(Preset("My Look", "Techniques=Z@z.fx"));
        Assert.Equal(["z.fx"], Assert.Single(ReShadePresets.List()).Check.Effects);

        ReShadePresets.Delete("My Look");
        Assert.Empty(ReShadePresets.List());
    }

    [Fact]
    public void An_invalid_file_is_rejected_with_the_reason_and_nothing_is_stored()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ReShadePresets.Import(Preset("bad", "[x]\r\nA=1")));

        Assert.Contains("not a ReShade preset", ex.Message);
        Assert.Empty(ReShadePresets.List());
    }

    [Fact]
    public async Task Applying_copies_the_preset_into_the_game_and_points_PresetPath_at_it()
    {
        ReShadePresets.Import(Preset("My Look"));

        var result = await Extras.ApplyPresetAsync(Game(), Target, "My Look", default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(Valid, File.ReadAllText(Path.Combine(Target, @"reshade-presets\My Look.ini")));
        Assert.Equal(@".\reshade-presets\My Look.ini", Ini("GENERAL", "PresetPath"));
        Assert.Equal(ComponentRegistry.ReShadePreset, Assert.Single(Manifest()!.Components).Id);
    }

    [Fact]
    public async Task Removing_the_preset_takes_the_file_and_the_key_away_again()
    {
        ReShadePresets.Import(Preset("My Look"));
        var before = Snapshot(Target);
        await Extras.ApplyPresetAsync(Game(), Target, "My Look", default);

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadePreset, default);

        Assert.Equal(before, Snapshot(Target));
        Assert.Equal("", Ini("GENERAL", "PresetPath"));
    }

    [Fact]
    public async Task A_users_override_of_PresetPath_wins()
    {
        ReShadePresets.Import(Preset("My Look"));
        var game = Game();
        Settings.For(game.Id).ReShade = [new IniOverride { Section = "GENERAL", Key = "PresetPath", Value = @".\mine.ini" }];

        var result = await Extras.ApplyPresetAsync(game, Target, "My Look", default);

        Assert.Equal("", Ini("GENERAL", "PresetPath"));
        Assert.Contains(result.Notes, n => n.Contains("override of PresetPath is kept"));
        Assert.True(File.Exists(Path.Combine(Target, @"reshade-presets\My Look.ini")));
    }

    [Fact]
    public async Task The_report_names_the_shaders_the_game_lacks_and_the_packs_that_have_them()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);
        ReShadePresets.Import(Preset("My Look"));
        var game = Game();
        var preset = ReShadePresets.Find("My Look")!;

        var needs = Extras.NeedsOf(game, Target, preset);

        Assert.Equal(["bloom.fx", "lilium__a.fx"], needs.Missing);
        // sweetfx has both files, so one pack covers the preset.
        Assert.Equal([Sweet], needs.Suggested.Select(p => p.Id));
        Assert.Empty(needs.NoPack);

        ExtrasPicks.SetPacks(Settings, [Lilium]);
        await Extras.ApplyShadersAsync(game, Target, null, default);
        var after = Extras.NeedsOf(game, Target, preset);
        Assert.Equal(["bloom.fx"], after.Missing);
        Assert.Equal([Sweet], after.Suggested.Select(p => p.Id));
    }

    [Fact]
    public void An_effect_no_pack_has_is_reported_apart()
    {
        Publish(packs: [LiliumPack()]);
        var preset = new PresetInfo("x", "x", ReShadePresets.Check("Techniques=A@lilium__a.fx,B@Mystery.fx"));

        var needs = Extras.NeedsOf(Game(), Target, preset);

        Assert.Equal([Lilium], needs.Suggested.Select(p => p.Id));
        Assert.Equal(["mystery.fx"], needs.NoPack);
    }

    [Fact]
    public void Effects_in_folders_on_the_search_path_count_as_installed()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nEffectSearchPaths=.\\fx\\**\r\n");
        Write(Path.Combine(Target, @"fx\deep\Bloom.fx"), "X");
        Write(Path.Combine(Tmp, @"outside\Evil.fx"), "X");

        var installed = ReShadePresets.InstalledEffects(Target, Root);

        Assert.Contains("Bloom.fx", installed);
        Assert.DoesNotContain("Evil.fx", installed);
    }

    [Fact]
    public async Task A_preset_changed_in_the_library_shows_as_an_update()
    {
        ReShadePresets.Import(Preset("My Look"));
        var game = Game();
        ExtrasPicks.SetGamePreset(Settings.For(game.Id), "My Look");
        await Extras.ApplyPresetAsync(game, Target, "My Look", default);
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));

        ReShadePresets.Import(Preset("My Look", Valid + "Extra=1\r\n"));

        Assert.Equal([ComponentRegistry.ReShadePreset], Extras.ComponentsToUpdate(game, Target, Manifest()));
    }

    [Fact]
    public async Task Nothing_is_applied_in_a_game_with_anti_cheat()
    {
        ReShadePresets.Import(Preset("My Look"));

        var result = await Extras.ApplyPresetAsync(Game(antiCheat: "EasyAntiCheat"), Target, "My Look", default);

        Assert.False(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(Target, "reshade-presets")));
    }
}
