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
}
