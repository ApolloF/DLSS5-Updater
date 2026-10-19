using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Tests;

/// <summary>One long-running operation at a time, and the game list merge after a scan.</summary>
public class BusyTests
{
    [Fact]
    public void SecondRun_IsRefused_WhileTheFirstIsGoing()
    {
        var gate = new BusyGate();
        var changes = 0;
        gate.Changed += () => changes++;

        var first = gate.TryEnter();
        Assert.NotNull(first);
        Assert.True(gate.IsBusy);
        Assert.Null(gate.TryEnter());

        first.Dispose();
        Assert.False(gate.IsBusy);
        Assert.Equal(2, changes);
        using var next = gate.TryEnter();
        Assert.NotNull(next);
    }

    [Fact]
    public void EndingAnOldRunTwice_DoesNotUnlockTheNextOne()
    {
        var gate = new BusyGate();
        var first = gate.TryEnter()!;
        first.Dispose();
        var second = gate.TryEnter()!;

        first.Dispose();
        Assert.True(gate.IsBusy);
        Assert.Null(gate.TryEnter());
        second.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task OverlappingRuns_NeverRunTogether()
    {
        var gate = new BusyGate();
        var release = new TaskCompletionSource();
        var started = 0;

        async Task Run()
        {
            using var lease = gate.TryEnter();
            if (lease is null) return;
            Interlocked.Increment(ref started);
            await release.Task;
        }

        // Started while the first is still writing, the way a second button or a queued action would be.
        var first = Run();
        var second = Run();
        var third = Run();
        Assert.True(second.IsCompleted && third.IsCompleted);
        Assert.True(gate.IsBusy);

        release.SetResult();
        await first;
        Assert.Equal(1, started);
        Assert.False(gate.IsBusy);
        await Run();
        Assert.Equal(2, started);
    }

    [Fact]
    public void Cancel_StopsOnlyTheCurrentRun()
    {
        var gate = new BusyGate();
        gate.Cancel();
        var first = gate.TryEnter()!;
        var token = first.Token;
        gate.Cancel();
        Assert.True(token.IsCancellationRequested);
        first.Dispose();

        using var second = gate.TryEnter()!;
        Assert.False(second.Token.IsCancellationRequested);
    }

    private sealed class Item(string id)
    {
        public string Id { get; } = id;
        public int Loads { get; set; }
    }

    [Fact]
    public void MergeById_DropsDuplicates_UpdatesKnown_AddsNew_RemovesGone()
    {
        var a = new Item("a");
        var dupe = new Item("a");
        var gone = new Item("gone");
        var list = new List<Item> { a, dupe, gone };

        MainViewModel.MergeById(list, ["a", "b", "b"], i => i.Id, s => s, (i, _) => i.Loads++, s => new Item(s));

        Assert.Equal(["a", "b"], list.Select(i => i.Id));
        Assert.Same(a, list[0]);
        Assert.Equal(1, a.Loads);
        Assert.Equal(0, dupe.Loads);
    }
}
