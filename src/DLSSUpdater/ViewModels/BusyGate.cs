namespace DLSSUpdater.ViewModels;

/// <summary>
/// Lets one long-running operation (scan, update check, install) run at a time. Each run gets its own cancellation
/// token, so Cancel stops exactly that run and work started later never inherits a cancelled or replaced token.
/// </summary>
public sealed class BusyGate
{
    private Lease? _current;

    public bool IsBusy => _current is not null;

    /// <summary>Raised when <see cref="IsBusy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Starts a run, or returns null when another one is still going. Dispose the lease to end the run.</summary>
    public Lease? TryEnter()
    {
        if (_current is not null) return null;
        _current = new Lease(this);
        Changed?.Invoke();
        return _current;
    }

    /// <summary>Cancels the current run, if any.</summary>
    public void Cancel() => _current?.Cts.Cancel();

    private void Release(Lease lease)
    {
        if (_current != lease) return;
        _current = null;
        Changed?.Invoke();
    }

    public sealed class Lease : IDisposable
    {
        private readonly BusyGate _gate;

        internal Lease(BusyGate gate) => _gate = gate;

        internal CancellationTokenSource Cts { get; } = new();
        public CancellationToken Token => Cts.Token;

        public void Dispose() => _gate.Release(this);
    }
}
