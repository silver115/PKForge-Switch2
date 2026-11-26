using System.Diagnostics;

namespace PKForge.App.Services;

/// <summary>
/// The Switch 2's form of PKForge's per-frame loop (Services/VsyncLoop.cs uses Android's Choreographer).
/// A dispatcher timer with no interval fires once on every frame of MauiLite's loop, which runs at the display rate,
/// and the step is told the real time since the last one, as on Android.
/// </summary>
public sealed class VsyncLoop
{
    private readonly Func<float, bool> _step;
    private readonly Stopwatch _clock = new();
    private IDispatcherTimer? _timer;

    /// <param name="step">Called each frame with the milliseconds since the last frame (capped at 50);
    /// returns whether the loop goes on.</param>
    public VsyncLoop(Func<float, bool> step) => _step = step;

    public bool IsRunning { get; private set; }

    /// <summary>Starts the loop (from the UI thread); nothing happens if it already runs.</summary>
    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _clock.Restart();
        _timer ??= CreateTimer();
        _timer.Start();
    }

    /// <summary>Stops the loop (from the UI thread): no further step runs.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _timer?.Stop();
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.GetForCurrentThread()!.CreateTimer();
        timer.Interval = TimeSpan.Zero;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => Frame();
        return timer;
    }

    private void Frame()
    {
        if (!IsRunning) return;
        var elapsed = (float)_clock.Elapsed.TotalMilliseconds;
        _clock.Restart();
        elapsed = elapsed <= 0 ? 1000f / 60f : Math.Min(elapsed, 50f);
        bool more;
        try { more = _step(elapsed); }
        catch (Exception error)
        {
            // As on Android: a failed step stops its loop, not the app.
            Stop();
            AppLog.Error("frame", "An animation step failed; it was stopped", error);
            return;
        }
        if (IsRunning && !more) Stop();
    }
}
