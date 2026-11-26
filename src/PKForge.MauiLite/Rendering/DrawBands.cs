using System.Runtime.ExceptionServices;

namespace PKForge.MauiLite;

/// <summary>
/// Threads that draw the bands of one frame at once, one per core. Each is pinned to its core for good: libnx starts
/// every thread on one core, and Horizon does not share a core between threads of equal priority. The thread that
/// calls <see cref="Run"/> (the UI thread, on the first core) draws band 0.
/// </summary>
public sealed class DrawBands : IDisposable
{
    readonly SemaphoreSlim[] _start;
    readonly CountdownEvent _finished = new(0);
    Action<int>? _band;
    Exception? _error;
    volatile bool _stopping;

    /// <param name="cores">The cores to draw on; the first is the calling thread's own.</param>
    /// <param name="pin">Moves the calling thread to a core.</param>
    public DrawBands(IReadOnlyList<int> cores, Action<int> pin)
    {
        ArgumentNullException.ThrowIfNull(cores);
        ArgumentNullException.ThrowIfNull(pin);
        if (cores.Count == 0)
            throw new ArgumentException("No cores to draw on.", nameof(cores));
        Count = cores.Count;
        _start = new SemaphoreSlim[Count - 1];
        for (var band = 1; band < Count; band++)
        {
            var (index, core) = (band, cores[band]);
            _start[index - 1] = new SemaphoreSlim(0);
            new Thread(() => Work(index, core, pin)) { IsBackground = true, Name = $"draw band {index}" }.Start();
        }
    }

    public int Count { get; }

    void Work(int band, int core, Action<int> pin)
    {
        try
        {
            pin(core);
        }
        catch (Exception error)
        {
            UiErrors.Report(error, $"draw band {band}");  // it still draws, on whatever core it started on
        }
        while (true)
        {
            _start[band - 1].Wait();
            if (_stopping)
                return;
            try
            {
                _band!(band);
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref _error, error, null);
            }
            finally
            {
                _finished.Signal();
            }
        }
    }

    /// <summary>Runs <paramref name="band"/> for every band (0 to <see cref="Count"/> − 1) at once, and returns when all are done.</summary>
    public void Run(Action<int> band)
    {
        ArgumentNullException.ThrowIfNull(band);
        _band = band;
        _error = null;
        _finished.Reset(Count - 1);
        foreach (var start in _start)
            start.Release();
        try
        {
            band(0);
        }
        finally
        {
            _finished.Wait();
        }
        if (_error is { } error)
            ExceptionDispatchInfo.Throw(error);
    }

    public void Dispose()
    {
        _stopping = true;
        foreach (var start in _start)
            start.Release();
    }
}
