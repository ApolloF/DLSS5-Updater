using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Add-ons and shader packs the user imports: copied byte for byte, offered next to the catalog's, installed the same way, never cleaned up.</summary>
public sealed class ExtrasLibraryTests : ExtrasTestBase
{
    private string Incoming(string name) => Path.Combine(Tmp, "incoming", name);

    [Theory]
    [InlineData("imported:..")]
    [InlineData("imported:")]
    [InlineData("imported:..\\..\\x")]
    [InlineData("imported:a/b")]
    public void Ids_that_are_not_a_plain_folder_name_are_rejected(string id)
    {
        var outside = Path.Combine(Tmp, "keep.txt");
        Write(outside, "x");
        ExtrasLibrary.Remove(id);
        Assert.True(File.Exists(outside));
        Assert.Throws<ArgumentException>(() => ExtrasLibrary.DirOf(id));
    }

    [Fact]
    public void An_add_on_file_is_copied_byte_for_byte_and_listed()
    {
        Write(Incoming("my_tool.addon64"), "ADDON-BYTES");

        var id = ExtrasLibrary.ImportAddon(Incoming("my_tool.addon64"));

        Assert.Equal("imported:my_tool", id);
        var addon = Assert.Single(ExtrasLibrary.Addons());
        Assert.True(addon.Imported);
        Assert.True(addon.IsInstallable);
        Assert.Equal("ADDON-BYTES", File.ReadAllText(Path.Combine(ExtrasLibrary.AddonsRoot, "my_tool", "my_tool.addon64")));
    }

    [Fact]
    public void A_zip_with_add_ons_is_unpacked_and_other_files_are_left_out()
    {
        Directory.CreateDirectory(Incoming(""));
        File.WriteAllBytes(Incoming("bundle.zip"), Zip(("bin/Tool.addon32", "T32"), ("bin/Tool.addon64", "T64"), ("readme.txt", "hi"), ("run.exe", "MZ")));

        ExtrasLibrary.ImportAddon(Incoming("bundle.zip"));

        var addon = Assert.Single(ExtrasLibrary.Addons());
        Assert.Equal(2, addon.Files[0].Payload!.Count);
        Assert.Equal(["x86", "x64"], addon.Files[0].Payload!.Select(p => p.Arch));
        Assert.False(File.Exists(Path.Combine(ExtrasLibrary.AddonsRoot, "bundle", "readme.txt")));
    }

    [Fact]
    public void Something_that_is_not_an_add_on_is_refused_and_nothing_is_stored()
    {
        Write(Incoming("notes.txt"), "x");
        Directory.CreateDirectory(Incoming(""));
        File.WriteAllBytes(Incoming("empty.zip"), Zip(("readme.txt", "x")));

        Assert.Throws<InvalidDataException>(() => ExtrasLibrary.ImportAddon(Incoming("notes.txt")));
        Assert.Throws<InvalidDataException>(() => ExtrasLibrary.ImportAddon(Incoming("empty.zip")));
        Assert.Empty(ExtrasLibrary.Addons());
    }

    [Fact]
    public async Task An_imported_add_on_installs_by_bitness_like_a_catalog_one()
    {
        Directory.CreateDirectory(Incoming(""));
        File.WriteAllBytes(Incoming("bundle.zip"), Zip(("Tool.addon32", "T32"), ("Tool.addon64", "T64")));
        var id = ExtrasLibrary.ImportAddon(Incoming("bundle.zip"));
        Extras.Reload();
        ExtrasPicks.SetAddons(Settings, [id]);

        var result = await Extras.ApplyAddonsAsync(Game(bits: 32), Target, null, default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal("T32", File.ReadAllText(Path.Combine(Target, "Tool.addon32")));
        Assert.False(File.Exists(Path.Combine(Target, "Tool.addon64")));
        Assert.Empty(Requests);
    }

    [Fact]
    public void A_shader_folder_is_imported_with_its_layout_and_the_effect_names()
    {
        Write(Path.Combine(Incoming("MyPack"), @"repo\Shaders\My.fx"), "FX");
        Write(Path.Combine(Incoming("MyPack"), @"repo\Shaders\inc\my.fxh"), "H");
        Write(Path.Combine(Incoming("MyPack"), @"repo\Textures\t.png"), "PNG");
        Write(Path.Combine(Incoming("MyPack"), @"repo\Other\ignored.txt"), "no");

        var id = ExtrasLibrary.ImportPack(Incoming("MyPack"));

        var pack = Assert.Single(ExtrasLibrary.Packs());
        Assert.Equal("imported:MyPack", id);
        Assert.True(pack.IsInstallable);
        Assert.Equal(["My.fx"], pack.Layout!.Effects);
        Assert.Equal("FX", File.ReadAllText(Path.Combine(ExtrasLibrary.PacksRoot, @"MyPack\Shaders\My.fx")));
        Assert.Equal("H", File.ReadAllText(Path.Combine(ExtrasLibrary.PacksRoot, @"MyPack\Shaders\inc\my.fxh")));
        Assert.False(File.Exists(Path.Combine(ExtrasLibrary.PacksRoot, @"MyPack\Other\ignored.txt")));
    }

    [Fact]
    public async Task An_imported_zip_pack_installs_into_the_game_and_is_part_of_the_catalog_lists()
    {
        Directory.CreateDirectory(Incoming(""));
        File.WriteAllBytes(Incoming("Pack.zip"), Zip(("Pack-1/Shaders/Mine.fx", "MINE"), ("Pack-1/Textures/m.png", "P")));
        var id = ExtrasLibrary.ImportPack(Incoming("Pack.zip"));
        Publish(packs: [LiliumPack()]);
        ExtrasPicks.SetPacks(Settings, [id]);

        Assert.Equal([Lilium, id], Extras.Catalog.Packs.Select(p => p.Id));
        var result = await Extras.ApplyShadersAsync(Game(), Target, null, default);

        Assert.True(result.Ok, result.Message);
        Assert.Equal("MINE", File.ReadAllText(Path.Combine(Target, @"reshade-shaders\Shaders\Mine.fx")));
        Assert.Equal("P", File.ReadAllText(Path.Combine(Target, @"reshade-shaders\Textures\m.png")));
        Assert.False(File.Exists(Path.Combine(Target, @"reshade-shaders\Shaders\lilium__a.fx")));
    }

    [Fact]
    public void Importing_again_replaces_it_and_only_removing_it_deletes_it()
    {
        Write(Path.Combine(Incoming("P"), @"Shaders\A.fx"), "1");
        var id = ExtrasLibrary.ImportPack(Incoming("P"));
        var first = ExtrasLibrary.Packs()[0].File!.Sha256;
        Write(Path.Combine(Incoming("P"), @"Shaders\A.fx"), "2");
        ExtrasLibrary.ImportPack(Incoming("P"));

        Assert.NotEqual(first, ExtrasLibrary.Packs()[0].File!.Sha256);

        ExtrasLibrary.Remove(id);
        Assert.Empty(ExtrasLibrary.Packs());
    }

    [Fact]
    public void A_folder_without_shaders_is_refused()
    {
        Write(Path.Combine(Incoming("Empty"), "readme.txt"), "x");

        Assert.Throws<InvalidDataException>(() => ExtrasLibrary.ImportPack(Incoming("Empty")));
        Assert.Empty(ExtrasLibrary.Packs());
    }

    [Fact]
    public void Imports_live_outside_the_folders_the_build_cleanup_looks_at()
    {
        Write(Incoming("t.addon64"), "x");
        ExtrasLibrary.ImportAddon(Incoming("t.addon64"));

        Assert.False(FileUtil.IsUnder(ExtrasLibrary.Root, AppPaths.Cache));
        Assert.False(FileUtil.IsUnder(ExtrasLibrary.CacheRoot, ExtrasLibrary.Root));
        var store = new ComponentStore(new GitHubClient(), () => true);
        Assert.DoesNotContain(store.StaleDownloads(Settings, []), i => FileUtil.IsUnder(i.Path, ExtrasLibrary.Root));
    }
}
