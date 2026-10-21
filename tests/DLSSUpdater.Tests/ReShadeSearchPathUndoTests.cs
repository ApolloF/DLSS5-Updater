using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>The add-ons and the shader packs share one effects folder on EffectSearchPaths: removing either must not strip it for the other.</summary>
public sealed class ReShadeSearchPathUndoTests : ExtrasTestBase
{
    private const string Entry = @".\reshade-shaders\Shaders\**";

    private async Task InstallBoth()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[GENERAL]\r\nEffectSearchPaths=.\\Other\\**\r\n");
        Publish([XinpZip()], [LiliumPack()]);
        ExtrasPicks.SetAddons(Settings, ["xinp"]);
        ExtrasPicks.SetPacks(Settings, [Lilium]);
        Assert.True((await Extras.ApplyAddonsAsync(Game(), Target, null, default)).Ok);
        Assert.True((await Extras.ApplyShadersAsync(Game(), Target, null, default)).Ok);
        Assert.Equal($@".\Other\**,{Entry}", Ini("GENERAL", "EffectSearchPaths"));
    }

    [Fact]
    public async Task Removing_the_addons_first_keeps_the_path_the_shader_packs_need()
    {
        await InstallBoth();

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeAddons, default);
        Assert.Equal($@".\Other\**,{Entry}", Ini("GENERAL", "EffectSearchPaths"));

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);
        Assert.Equal(@".\Other\**", Ini("GENERAL", "EffectSearchPaths"));
    }

    [Fact]
    public async Task Removing_the_shader_packs_first_keeps_the_path_the_addons_need()
    {
        await InstallBoth();

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeShaders, default);
        Assert.Equal($@".\Other\**,{Entry}", Ini("GENERAL", "EffectSearchPaths"));

        await Extras.RemoveAsync(Game(), Target, ComponentRegistry.ReShadeAddons, default);
        Assert.Equal(@".\Other\**", Ini("GENERAL", "EffectSearchPaths"));
    }

    [Fact]
    public void The_list_helpers_find_and_remove_only_the_named_entries()
    {
        Assert.Equal([Entry], ReShadeLayout.AddedEntries($@".\Other\**,{Entry}", @".\Other\**"));
        Assert.Equal(@".\Other\**", ReShadeLayout.RemoveEntries($@".\Other\**,{Entry}", [Entry]));
        Assert.Null(ReShadeLayout.RemoveEntries(Entry, [Entry]));
    }
}
