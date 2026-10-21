using DLSSUpdater.Core;
using DLSSUpdater.Scan;

namespace DLSSUpdater.Tests;

/// <summary>Shader packs from the catalog: deployed into a managed folder with a marker, search paths set, a user's folder kept, uninstall exact.</summary>
public sealed class ShaderPackTests : ExtrasTestBase
{
    private string Shaders => Path.Combine(Target, @"reshade-shaders\Shaders");

    private async Task<ExtrasResult> Apply(GameInfo? game = null) => await Extras.ApplyShadersAsync(game ?? Game(), Target, null, default);

    [Fact]
    public async Task The_catalog_defaults_are_installed_until_the_user_chooses()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);

        Assert.Equal([Lilium], ExtrasPicks.PackIds(Settings, null, Extras.Catalog.PackList));
        var result = await Apply();

        Assert.True(result.Ok, result.Message);
        Assert.True(File.Exists(Path.Combine(Shaders, "lilium__a.fx")));
        Assert.False(File.Exists(Path.Combine(Shaders, "Bloom.fx")));
    }

    [Fact]
    public async Task A_pack_keeps_its_layout_and_leaves_out_other_files_executables_and_hidden_files()
    {
        Publish(packs: [LiliumPack()]);

        await Apply();

        Assert.Equal("LILIUM-A", File.ReadAllText(Path.Combine(Shaders, "lilium__a.fx")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(Shaders, @"Lilium\b.fxh")));
        Assert.Equal("PNG", File.ReadAllText(Path.Combine(Target, @"reshade-shaders\Textures\n.png")));
        Assert.False(File.Exists(Path.Combine(Shaders, "evil.dll")));
        Assert.False(File.Exists(Path.Combine(Shaders, ".hidden")));
        Assert.False(File.Exists(Path.Combine(Target, @"reshade-shaders\README.md")));
    }

    [Fact]
    public async Task A_marker_file_is_placed_and_tracked_with_every_other_file()
    {
        Publish(packs: [LiliumPack()]);

        await Apply();

        var marker = Path.Combine(Target, @"reshade-shaders", ReShadeLayout.Marker);
        Assert.Contains(Lilium, File.ReadAllText(marker));
        var c = Manifest()!.Component(ComponentRegistry.ReShadeShaders)!;
        Assert.Equal(4, c.Files.Count);
        Assert.Contains(c.Files, f => f.Path.EndsWith(ReShadeLayout.Marker));
        Assert.All(c.Files, f => Assert.Equal(FileUtil.Sha256(Path.Combine(Root, f.Path)), f.Sha256));
        Assert.Empty(Directory.GetFiles(AppPaths.Cache, "reshade-extras-marker-*"));
    }

    [Fact]
    public async Task Removing_takes_out_exactly_what_was_placed()
    {
        Publish(packs: [LiliumPack()]);
        var before = Snapshot(Target);

        await Apply();
        var result = await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);

        Assert.True(result.Ok);
        Assert.Equal(before, Snapshot(Target));
        Assert.False(Directory.Exists(Path.Combine(Target, "reshade-shaders")));
    }

    [Fact]
    public async Task A_file_the_user_added_to_our_folder_survives_removal()
    {
        Publish(packs: [LiliumPack()]);
        await Apply();
        Write(Path.Combine(Shaders, "mine.fx"), "MINE");

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);

        Assert.Equal("MINE", File.ReadAllText(Path.Combine(Shaders, "mine.fx")));
        Assert.False(File.Exists(Path.Combine(Shaders, "lilium__a.fx")));
    }

    [Fact]
    public async Task A_folder_the_user_already_has_is_kept_and_the_packs_go_beside_it()
    {
        Write(Path.Combine(Shaders, "UserEffect.fx"), "USER");
        Write(Path.Combine(Target, @"reshade-shaders\Textures\user.png"), "UPNG");
        Publish(packs: [LiliumPack()]);
        var users = Snapshot(Path.Combine(Target, "reshade-shaders"));

        var result = await Apply();

        Assert.True(result.Ok, result.Message);
        Assert.Equal(users, Snapshot(Path.Combine(Target, "reshade-shaders")));
        Assert.Equal("LILIUM-A", File.ReadAllText(Path.Combine(Target, @"reshade-shaders-managed\Shaders\lilium__a.fx")));
        Assert.Contains(result.Notes, n => n.Contains("left exactly as it is") && n.Contains("reshade-shaders-managed"));
        Assert.Contains(@".\reshade-shaders\Shaders\**", Ini("GENERAL", "EffectSearchPaths"));
        Assert.Contains(@".\reshade-shaders-managed\Shaders\**", Ini("GENERAL", "EffectSearchPaths"));

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);
        Assert.Equal(users, Snapshot(Path.Combine(Target, "reshade-shaders")));
        Assert.False(Directory.Exists(Path.Combine(Target, "reshade-shaders-managed")));
        Assert.DoesNotContain("managed", Ini("GENERAL", "EffectSearchPaths"));
    }

    [Fact]
    public async Task Search_paths_are_added_when_missing_and_restored_on_removal()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nPerformanceMode=0\r\n");
        Publish(packs: [LiliumPack()]);

        await Apply();

        Assert.Equal(@".\reshade-shaders\Shaders\**", Ini("GENERAL", "EffectSearchPaths"));
        Assert.Equal(@".\reshade-shaders\Textures\**", Ini("GENERAL", "TextureSearchPaths"));

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);
        Assert.Equal("", Ini("GENERAL", "EffectSearchPaths"));
        Assert.Equal("0", Ini("GENERAL", "PerformanceMode"));
    }

    [Fact]
    public async Task A_users_ReShade_ini_override_of_the_search_path_wins()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nPerformanceMode=0\r\n");
        Publish(packs: [LiliumPack()]);
        var game = Game();
        Settings.For(game.Id).ReShade = [new IniOverride { Section = "GENERAL", Key = "EffectSearchPaths", Value = @".\mine\**" }];

        var result = await Apply(game);

        Assert.Equal("", Ini("GENERAL", "EffectSearchPaths"));
        Assert.Equal(@".\reshade-shaders\Textures\**", Ini("GENERAL", "TextureSearchPaths"));
        Assert.Contains(result.Notes, n => n.Contains("override of EffectSearchPaths is kept"));
        Assert.True(File.Exists(Path.Combine(Shaders, "lilium__a.fx")));
    }

    [Fact]
    public async Task A_file_two_packs_share_comes_from_the_first_and_a_difference_is_reported()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);
        ExtrasPicks.SetPacks(Settings, [Lilium, Sweet]);

        var result = await Apply();

        Assert.Equal("LILIUM-A", File.ReadAllText(Path.Combine(Shaders, "lilium__a.fx")));
        Assert.Equal("BLOOM", File.ReadAllText(Path.Combine(Shaders, "Bloom.fx")));
        Assert.Contains(result.Notes, n => n.StartsWith("sweetfx") && n.Contains("lilium__a.fx") && n.Contains("differs"));
    }

    [Fact]
    public async Task An_identical_file_in_two_packs_is_not_a_conflict()
    {
        Publish(packs: [LiliumPack(), SweetPack(lilium: "LILIUM-A")]);
        ExtrasPicks.SetPacks(Settings, [Lilium, Sweet]);

        var result = await Apply();

        Assert.DoesNotContain(result.Notes, n => n.Contains("differs"));
    }

    [Fact]
    public async Task A_file_another_component_owns_is_not_overwritten()
    {
        Publish([XinpZip()], [PackOf("fxpack", Zip(("P/Shaders/Xinp/Xinp.fx", "PACK")), "P/Shaders/", null, ["Xinp.fx"])]);
        ExtrasPicks.SetAddons(Settings, ["xinp"]);
        await Extras.ApplyAddonsAsync(Game(), Target, null, default);
        ExtrasPicks.SetPacks(Settings, ["fxpack"]);

        var result = await Apply();

        Assert.Equal("XFX-1", File.ReadAllText(Path.Combine(Shaders, @"Xinp\Xinp.fx")));
        Assert.Contains(result.Notes, n => n.Contains("belongs to ReShade add-ons"));
    }

    [Fact]
    public async Task A_manual_selection_wins_and_an_empty_one_removes_the_packs()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);
        ExtrasPicks.SetPacks(Settings, [Sweet]);
        await Apply();
        Assert.True(File.Exists(Path.Combine(Shaders, "Bloom.fx")));
        Assert.False(File.Exists(Path.Combine(Shaders, @"Lilium\b.fxh")));

        ExtrasPicks.SetPacks(Settings, []);
        await Apply();

        Assert.Null(Manifest()?.Component(ComponentRegistry.ReShadeShaders));
        Assert.False(File.Exists(Path.Combine(Shaders, "Bloom.fx")));
    }

    [Fact]
    public async Task Reset_to_auto_goes_back_to_the_defaults_and_a_game_pick_can_be_reset_to_follow_Settings()
    {
        Publish(packs: [LiliumPack(), SweetPack()]);
        var game = Game();
        ExtrasPicks.SetPacks(Settings, [Sweet]);
        ExtrasPicks.SetGamePacks(Settings.For(game.Id), [Lilium, Sweet]);

        Assert.Equal([Lilium, Sweet], ExtrasPicks.PackIds(Settings, Settings.For(game.Id), Extras.Catalog.PackList).Order());
        ExtrasPicks.SetGamePacks(Settings.For(game.Id), null);
        Assert.Equal([Sweet], ExtrasPicks.PackIds(Settings, Settings.For(game.Id), Extras.Catalog.PackList));
        Settings.Components = null;
        Assert.Equal([Lilium], ExtrasPicks.PackIds(Settings, Settings.For(game.Id), Extras.Catalog.PackList));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_new_pack_hash_shows_as_an_update_and_a_second_run_changes_nothing()
    {
        Publish(packs: [LiliumPack("V1")]);
        var game = Game();
        await Apply(game);
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));

        Publish(packs: [LiliumPack("V2")]);
        Assert.Equal([ComponentRegistry.ReShadeShaders], Extras.ComponentsToUpdate(game, Target, Manifest()));
        await Extras.UpdateAsync(game, Target, null, default);

        Assert.Equal("V2", File.ReadAllText(Path.Combine(Shaders, "lilium__a.fx")));
        Assert.Empty(Extras.ComponentsToUpdate(game, Target, Manifest()));
    }

    [Fact]
    public async Task Nothing_is_installed_in_a_game_with_anti_cheat()
    {
        Publish(packs: [LiliumPack()]);

        var result = await Apply(Game(antiCheat: "BattlEye"));

        Assert.False(result.Ok);
        Assert.False(Directory.Exists(Path.Combine(Target, "reshade-shaders")));
    }

    [Fact]
    public async Task A_second_apply_downloads_nothing_again()
    {
        Publish(packs: [LiliumPack()]);
        await Apply();
        var requests = Requests.Count;

        await Apply();

        Assert.Equal(requests, Requests.Count);
    }
}
