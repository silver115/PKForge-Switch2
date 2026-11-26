using System.Collections.Concurrent;
using Microsoft.Maui.Dispatching;

namespace PKForge.MauiLite;

/// <summary>
/// The one UI thread's work queue and timers. <see cref="RunOnce"/> runs work posted before it was called, then due timers.
/// While it runs, <c>await</c> resumes on this loop. A throwing action goes to <see cref="UiErrors"/> and the loop goes on.
/// </summary>
public sealed class UiLoop : IDispatcher
{
    public static UiLoop Current { get; } = new();

    readonly ConcurrentQueue<Action> _posted = new();
    readonly List<LoopTimer> _timers = [];
    readonly Lock _timerGate = new();
    readonly LoopContext _context;
    int _uiThread = -1;
    TimeSpan _now;

    public UiLoop() => _context = new LoopContext(this);

    /// <summary>True on the thread that runs this loop.</summary>
    public bool IsUiThread => Environment.CurrentManagedThreadId == _uiThread;

    /// <summary>True once a thread has run or attached to this loop.</summary>
    internal bool IsAttached => _uiThread != -1;

    /// <summary>The loop time of the last <see cref="RunOnce"/>.</summary>
    internal TimeSpan Now => _now;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
    }

    /// <summary>Makes the calling thread this loop's thread for good: its synchronization context stays installed.</summary>
    public void Attach()
    {
        _uiThread = Environment.CurrentManagedThreadId;
        SynchronizationContext.SetSynchronizationContext(_context);
    }

    public void RunOnce(TimeSpan now)
    {
        _uiThread = Environment.CurrentManagedThreadId;
        _now = now;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        try
        {
            // Only work already queued: an action that posts again runs next frame, so a loop of posts cannot hang a frame.
            for (var count = _posted.Count; count > 0 && _posted.TryDequeue(out var action); count--)
                Run(action, "posted work");
            LoopTimer[] due;
            lock (_timerGate)
                due = [.. _timers.Where(timer => timer.IsRunning && now >= timer.Due)];
            foreach (var timer in due)
            {
                timer.Due = now + timer.Interval;
                if (!timer.IsRepeating)
                    timer.Stop();
                Run(timer.Fire, "timer");
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    static void Run(Action action, string where)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            UiErrors.Report(error, where);
        }
    }

    public bool IsDispatchRequired => !IsUiThread;

    public bool Dispatch(Action action)
    {
        Post(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timer = new LoopTimer(this) { Interval = delay, IsRepeating = false };
        timer.Tick += (_, _) => action();
        timer.Start();
        return true;
    }

    public IDispatcherTimer CreateTimer() => new LoopTimer(this);

    void Add(LoopTimer timer)
    {
        lock (_timerGate)
            if (!_timers.Contains(timer))
                _timers.Add(timer);
    }

    void Remove(LoopTimer timer)
    {
        lock (_timerGate)
            _timers.Remove(timer);
    }

    sealed class LoopTimer(UiLoop loop) : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; } = true;
        public bool IsRunning { get; private set; }
        internal TimeSpan Due { get; set; }

        public event EventHandler? Tick;

        public void Start()
        {
            Due = loop.Now + Interval;
            IsRunning = true;
            loop.Add(this);
        }

        public void Stop()
        {
            IsRunning = false;
            loop.Remove(this);
        }

        internal void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    sealed class LoopContext(UiLoop loop) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => loop.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (loop.IsUiThread)
                d(state);
            else
                loop.Post(() => d(state));
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
