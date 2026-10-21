using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Where ReShade looks in a game: the add-on folder, the shader folder (ours or the user's) and the search path lists.</summary>
public sealed class ReShadeLayoutTests : ExtrasTestBase
{
    [Fact]
    public void Without_an_ini_or_a_key_the_add_on_folder_is_the_install_folder()
    {
        File.Delete(Path.Combine(Target, "ReShade.ini"));
        Assert.Equal((Target, null), ReShadeLayout.AddonDir(Target, Root));

        Write(Path.Combine(Target, "ReShade.ini"), "[ADDON]\r\nDisabledAddons=x\r\n");
        Assert.Equal((Target, null), ReShadeLayout.AddonDir(Target, Root));
    }

    [Fact]
    public void A_relative_AddonPath_is_resolved_against_the_ini_folder()
    {
        Write(Path.Combine(Target, "ReShade.ini"), "[ADDON]\r\nAddonPath=.\\plugins\\reshade\r\n");

        Assert.Equal((Path.Combine(Target, @"plugins\reshade"), null), ReShadeLayout.AddonDir(Target, Root));
    }

    [Theory]
    [InlineData(@"..\..\elsewhere")]
    [InlineData(@"C:\Windows")]
    public void An_AddonPath_that_leaves_the_game_is_refused(string path)
    {
        Write(Path.Combine(Target, "ReShade.ini"), $"[ADDON]\r\nAddonPath={path}\r\n");

        var (dir, why) = ReShadeLayout.AddonDir(Target, Root);

        Assert.Null(dir);
        Assert.Contains("outside the game", why);
    }

    [Fact]
    public void Our_own_folder_is_used_when_it_is_missing_marked_or_holds_only_our_files()
    {
        Assert.False(ReShadeLayout.ResolveShaderFolder(Target, Root, null).UserFolderKept);

        Write(Path.Combine(Target, @"reshade-shaders\Shaders\a.fx"), "A");
        Assert.True(ReShadeLayout.ResolveShaderFolder(Target, Root, null).UserFolderKept);

        Write(Path.Combine(Target, "reshade-shaders", ReShadeLayout.Marker), "{}");
        var marked = ReShadeLayout.ResolveShaderFolder(Target, Root, null);
        Assert.False(marked.UserFolderKept);
        Assert.Equal("reshade-shaders", marked.Name);
    }

    [Fact]
    public void A_user_folder_goes_to_the_managed_folder_next_to_it()
    {
        Write(Path.Combine(Target, @"reshade-shaders\Shaders\mine.fx"), "M");

        var folder = ReShadeLayout.ResolveShaderFolder(Target, Root, null);

        Assert.True(folder.UserFolderKept);
        Assert.Equal(ReShadeLayout.ManagedFolder, folder.Name);
        Assert.Equal(Path.Combine(Target, ReShadeLayout.ManagedFolder, "Shaders"), folder.ShadersDir);
    }

    [Theory]
    [InlineData(@".\reshade-shaders\Shaders\**", @".\reshade-shaders\Shaders\**", @".\reshade-shaders\Shaders\**")]
    [InlineData(@".\reshade-shaders\Shaders", @".\reshade-shaders\Shaders\**", @".\reshade-shaders\Shaders")]
    [InlineData(@"./Reshade-Shaders/Shaders/**", @".\reshade-shaders\Shaders\**", @"./Reshade-Shaders/Shaders/**")]
    [InlineData(@".\Other\**", @".\reshade-shaders\Shaders\**", @".\Other\**,.\reshade-shaders\Shaders\**")]
    [InlineData("", @".\reshade-shaders\Shaders\**", @".\reshade-shaders\Shaders\**")]
    [InlineData(null, @".\reshade-shaders\Shaders\**", @".\reshade-shaders\Shaders\**")]
    public void A_folder_is_added_to_a_search_path_list_only_when_it_is_missing(string? existing, string entry, string expected) =>
        Assert.Equal(expected, ReShadeLayout.MergeSearchPath(existing, entry));

    [Fact]
    public void A_search_path_that_already_covers_the_folder_needs_no_edit()
    {
        var folder = ReShadeLayout.ResolveShaderFolder(Target, Root, null);

        Assert.Empty(ReShadeLayout.SearchPathEdits(Target, folder, true, true));
    }
}
