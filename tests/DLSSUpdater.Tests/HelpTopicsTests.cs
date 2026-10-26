using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

public sealed class HelpTopicsTests
{
    private const int MaxTipLength = 110;

    [Fact]
    public void Every_topic_has_a_short_tip_that_is_not_the_body()
    {
        var problems = HelpTopics.All.SelectMany(t => Problems(t)).ToList();
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    private static IEnumerable<string> Problems(HelpTopic t)
    {
        if (string.IsNullOrWhiteSpace(t.Tip)) yield return $"{t.Id} has no tip";
        if (t.Tip.Length > MaxTipLength) yield return $"{t.Id} tip is {t.Tip.Length} characters";
        if (t.Tip == t.Body || t.Tip.Length >= t.Body.Length) yield return $"{t.Id} tip is not shorter than its body";
    }

    [Fact]
    public void Topic_ids_are_unique_and_tip_returns_the_short_text()
    {
        Assert.Equal(HelpTopics.All.Length, HelpTopics.All.Select(t => t.Id).Distinct().Count());
        var luma = HelpTopics.Get("luma")!;
        Assert.Equal(luma.Tip, HelpTopics.Tip("luma"));
        Assert.Equal("", HelpTopics.Tip("no-such-topic"));
    }
}
