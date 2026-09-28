using ECommerce.Worker.Options;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Resilience;

public sealed class WorkerOperationDrain(IOptions<WorkerRuntimeOptions> options) : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _operationCancellation = new();
    private readonly TaskCompletionSource _drained = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.Value.ShutdownDrainSeconds);
    private int _inFlight;
    private bool _stopping;

    public bool TryBegin(out OperationLease lease)
    {
        lock (_sync)
        {
            if (_stopping)
            {
                lease = null!;
                return false;
            }

            _inFlight++;
            lease = new OperationLease(this, _operationCancellation.Token);
            return true;
        }
    }

    public void StopAccepting()
    {
        lock (_sync)
        {
            if (_stopping)
                return;
            _stopping = true;
            _operationCancellation.CancelAfter(_timeout);
            if (_inFlight == 0)
                _drained.TrySetResult();
        }
    }

    public async Task<bool> WaitForDrainAsync(CancellationToken cancellationToken = default)
    {
        StopAccepting();
        try
        {
            await _drained.Task.WaitAsync(_timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void Complete()
    {
        lock (_sync)
        {
            _inFlight--;
            if (_stopping && _inFlight == 0)
                _drained.TrySetResult();
        }
    }

    public void Dispose()
    {
        _operationCancellation.Dispose();
    }

    public sealed class OperationLease(
        WorkerOperationDrain owner,
        CancellationToken cancellationToken) : IDisposable
    {
        private int _disposed;
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Complete();
        }
    }
}
