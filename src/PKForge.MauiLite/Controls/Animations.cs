using System.Collections;
using System.Diagnostics;
using Microsoft.Maui.Dispatching;
using PKForge.MauiLite;

namespace Microsoft.Maui.Controls;

public interface IAnimatable
{
    void BatchBegin();
    void BatchCommit();
}

/// <summary>MAUI's animation: a value run from a start to an end, with child animations over parts of its time.</summary>
public class Animation : IEnumerable
{
    readonly Action<double>? _step;
    readonly double _start;
    readonly double _end;
    readonly Easing _easing;
    readonly List<(double Begin, double Finish, Animation Child)> _children = [];

    public Animation() : this(null)
    {
    }

    public Animation(Action<double>? callback, double start = 0.0f, double end = 1.0f, Easing? easing = null, Action? finished = null)
    {
        _step = callback;
        _start = start;
        _end = end;
        _easing = easing ?? Easing.Linear;
        Finished = finished;
    }

    internal Action? Finished { get; }

    public void Add(double beginAt, double finishAt, Animation animation) => _children.Add((beginAt, finishAt, animation));

    public Animation Insert(double beginAt, double finishAt, Animation animation)
    {
        Add(beginAt, finishAt, animation);
        return this;
    }

    public Animation WithConcurrent(Animation animation, double beginAt = 0.0f, double finishAt = 1.0f) => Insert(beginAt, finishAt, animation);

    public void Commit(IAnimatable owner, string name, uint rate = 16, uint length = 250, Easing? easing = null,
        Action<double, bool>? finished = null, Func<bool>? repeat = null) =>
        Animations.Start(owner, name, this, length, easing, finished, repeat);

    public IEnumerator GetEnumerator() => _children.Select(child => child.Child).GetEnumerator();

    /// <summary>Steps this animation and its children to time <paramref name="t"/> (0 to 1); returns its value.</summary>
    internal double Apply(double t)
    {
        var value = _start + (_end - _start) * _easing.Ease(t);
        _step?.Invoke(value);
        foreach (var (begin, finish, child) in _children)
        {
            if (t < begin)
                continue;
            child.Apply(finish <= begin ? 1 : Math.Min(1, (t - begin) / (finish - begin)));
        }
        return value;
    }

    internal void Complete()
    {
        Finished?.Invoke();
        foreach (var (_, _, child) in _children)
            child.Complete();
    }
}

public static class AnimationExtensions
{
    public static bool AbortAnimation(this IAnimatable self, string handle) => Animations.Abort(self, handle);

    public static bool AnimationIsRunning(this IAnimatable self, string handle) => Animations.IsRunning(self, handle);

    public static void Animate(this IAnimatable self, string name, Action<double> callback, double start, double end, uint rate = 16, uint length = 250,
        Easing? easing = null, Action<double, bool>? finished = null, Func<bool>? repeat = null) =>
        new Animation(callback, start, end).Commit(self, name, rate, length, easing, finished, repeat);

    public static void Animate(this IAnimatable self, string name, Action<double> callback, uint rate = 16, uint length = 250, Easing? easing = null,
        Action<double, bool>? finished = null, Func<bool>? repeat = null) =>
        self.Animate(name, callback, 0, 1, rate, length, easing, finished, repeat);
}

/// <summary>MAUI's view animations. Each returns true when it was cancelled.</summary>
public static class ViewExtensions
{
    static readonly string[] Names = ["FadeTo", "TranslateTo", "ScaleTo", "ScaleXTo", "ScaleYTo", "RotateTo", "LayoutTo"];

    public static Task<bool> FadeToAsync(this VisualElement view, double opacity, uint length = 250, Easing? easing = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Run(view, "FadeTo", value => view.Opacity = value, view.Opacity, opacity, length, easing);
    }

    public static Task<bool> TranslateToAsync(this VisualElement view, double x, double y, uint length = 250, Easing? easing = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var (fromX, fromY) = (view.TranslationX, view.TranslationY);
        return Run(view, "TranslateTo", t =>
        {
            view.TranslationX = fromX + (x - fromX) * t;
            view.TranslationY = fromY + (y - fromY) * t;
        }, 0, 1, length, easing);
    }

    public static Task<bool> ScaleToAsync(this VisualElement view, double scale, uint length = 250, Easing? easing = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Run(view, "ScaleTo", value => view.Scale = value, view.Scale, scale, length, easing);
    }

    public static Task<bool> RotateToAsync(this VisualElement view, double rotation, uint length = 250, Easing? easing = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        return Run(view, "RotateTo", value => view.Rotation = value, view.Rotation, rotation, length, easing);
    }

    public static void CancelAnimations(this VisualElement view)
    {
        ArgumentNullException.ThrowIfNull(view);
        foreach (var name in Names)
            view.AbortAnimation(name);
    }

    static Task<bool> Run(VisualElement view, string name, Action<double> step, double from, double to, uint length, Easing? easing)
    {
        var done = new TaskCompletionSource<bool>();
        new Animation(step, from, to, easing).Commit(view, name, 16, length, finished: (_, cancelled) => done.TrySetResult(cancelled));
        return done.Task;
    }
}

/// <summary>The running animations, by owner and name, each stepped once per frame on the UI loop.</summary>
static class Animations
{
    sealed class Running(Animation animation, uint length, Easing? easing, Action<double, bool>? finished, Func<bool>? repeat, IDispatcherTimer timer)
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public Animation Animation { get; } = animation;
        public uint Length { get; } = length;
        public Easing Easing { get; } = easing ?? Easing.Linear;
        public Action<double, bool>? Finished { get; } = finished;
        public Func<bool>? Repeat { get; } = repeat;
        public IDispatcherTimer Timer { get; } = timer;
        public double Value { get; set; }
    }

    static readonly Dictionary<(IAnimatable Owner, string Name), Running> Active = [];

    public static void Start(IAnimatable owner, string name, Animation animation, uint length, Easing? easing, Action<double, bool>? finished, Func<bool>? repeat)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // As MAUI: committing a name that runs replaces the running one.
        Abort(owner, name);
        var timer = UiLoop.Current.CreateTimer();
        timer.Interval = TimeSpan.Zero;
        var running = new Running(animation, length, easing, finished, repeat, timer);
        Active[(owner, name)] = running;
        timer.Tick += (_, _) => Step(owner, name, running);
        timer.Start();
    }

    static void Step(IAnimatable owner, string name, Running running)
    {
        var t = running.Length == 0 ? 1 : Math.Min(1, running.Clock.Elapsed.TotalMilliseconds / running.Length);
        running.Value = running.Animation.Apply(running.Easing.Ease(t));
        if (t < 1)
            return;
        if (running.Repeat?.Invoke() == true)
        {
            running.Clock.Restart();
            return;
        }
        running.Timer.Stop();
        Active.Remove((owner, name));
        running.Finished?.Invoke(running.Value, false);
        running.Animation.Complete();
    }

    public static bool Abort(IAnimatable owner, string name)
    {
        if (!Active.Remove((owner, name), out var running))
            return false;
        running.Timer.Stop();
        running.Finished?.Invoke(running.Value, true);
        return true;
    }

    public static bool IsRunning(IAnimatable owner, string name) => Active.ContainsKey((owner, name));
}
