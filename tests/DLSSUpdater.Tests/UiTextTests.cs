using DLSSUpdater.Core;
using DLSSUpdater.ViewModels;
using DLSSUpdater.Views;

namespace DLSSUpdater.Tests;

/// <summary>The words the UI shows where a colour or a count used to carry the meaning alone.</summary>
public class UiTextTests
{
    [Theory]
    [InlineData(RowState.Current, "Up to date")]
    [InlineData(RowState.Update, "Update available")]
    [InlineData(RowState.Missing, "Not available")]
    [InlineData(RowState.None, "Not applicable")]
    public void StateDots_have_a_text_for_every_state(RowState state, string text)
    {
        Assert.Equal(text, StateToText.Describe(state));
        Assert.Equal(text, new StateToText().Convert(state, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StateDots_without_a_state_are_not_applicable()
    {
        Assert.Equal("Not applicable", new StateToText().Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(3, 3, false, null)]
    [InlineData(3, 1, true, null)]
    [InlineData(3, 0, false, "No games match the search or filter.")]
    [InlineData(0, 0, true, "Looking for games…")]
    [InlineData(0, 0, false, "No games found yet. Add a game or a library folder below.")]
    public void Empty_game_list_says_why(int total, int shown, bool scanning, string? expected)
    {
        Assert.Equal(expected, MainViewModel.EmptyListText(total, shown, scanning));
    }

    [Fact]
    public void Release_chip_name_mentions_a_new_release()
    {
        var news = new ReleaseNewsViewModel(Component.Dlss, "DLSS", new AppSettings());
        Assert.Equal("Release notes for DLSS", news.AutomationName);
        news.IsNew = true;
        Assert.Equal("Release notes for DLSS, new release", news.AutomationName);
    }

    [Theory]
    [InlineData(1, "1 game has updates available")]
    [InlineData(4, "4 games have updates available")]
    public void Update_count_uses_a_real_plural(int n, string expected) =>
        Assert.Equal(expected, MainViewModel.UpdatesAvailableText(n));

    [Theory]
    [InlineData(1, 0, "Updated 1 game")]
    [InlineData(3, 0, "Updated 3 games")]
    [InlineData(3, 1, "1 game couldn't be updated. Details are in the log.")]
    [InlineData(3, 2, "2 games couldn't be updated. Details are in the log.")]
    public void Update_all_result_uses_a_real_plural(int updated, int failed, string expected) =>
        Assert.Equal(expected, MainViewModel.UpdateAllResultText(updated, failed));

    [Fact]
    public void No_view_model_has_a_picker_label_with_brackets_or_game_s()
    {
        var dir = Path.Combine(RepoRoot(), "src", "DLSSUpdater", "ViewModels");
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            // Literal labels too, not just interpolated ones: pickers say "Default · 4".
            Assert.DoesNotContain("\"Default (", text);
            Assert.DoesNotContain("\"Inherit (", text);
            Assert.DoesNotContain("game(s)", text);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DLSSUpdater.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }
}
