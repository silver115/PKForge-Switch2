using PKForge.MauiLite;

namespace Microsoft.Maui.Dispatching
{
    public interface IDispatcher
    {
        bool IsDispatchRequired { get; }
        bool Dispatch(Action action);
        bool DispatchDelayed(TimeSpan delay, Action action);
        IDispatcherTimer CreateTimer();
    }

    public interface IDispatcherTimer
    {
        TimeSpan Interval { get; set; }
        bool IsRepeating { get; set; }
        bool IsRunning { get; }
        event EventHandler Tick;
        void Start();
        void Stop();
    }

    /// <summary>MAUI's dispatcher lookup. MauiLite has one UI thread, so every thread gets its loop.</summary>
    public static class Dispatcher
    {
        public static IDispatcher? GetForCurrentThread() => UiLoop.Current;
    }

    public static class DispatcherExtensions
    {
        public static Task<T> DispatchAsync<T>(this IDispatcher dispatcher, Func<T> func)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            var done = new TaskCompletionSource<T>();
            dispatcher.Dispatch(() =>
            {
                try { done.SetResult(func()); }
                catch (Exception error) { done.SetException(error); }
            });
            return done.Task;
        }

        public static Task DispatchAsync(this IDispatcher dispatcher, Action action) =>
            dispatcher.DispatchAsync(() => { action(); return true; });

        public static Task<T> DispatchAsync<T>(this IDispatcher dispatcher, Func<Task<T>> func)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            var done = new TaskCompletionSource<T>();
            dispatcher.Dispatch(async () =>
            {
                try { done.SetResult(await func()); }
                catch (Exception error) { done.SetException(error); }
            });
            return done.Task;
        }

        public static Task DispatchAsync(this IDispatcher dispatcher, Func<Task> func) =>
            dispatcher.DispatchAsync(async () => { await func(); return true; });

        /// <summary>Calls <paramref name="callback"/> every <paramref name="interval"/> until it returns false.</summary>
        public static void StartTimer(this IDispatcher dispatcher, TimeSpan interval, Func<bool> callback)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);
            var timer = dispatcher.CreateTimer();
            timer.Interval = interval;
            timer.IsRepeating = true;
            timer.Tick += (_, _) =>
            {
                if (!callback())
                    timer.Stop();
            };
            timer.Start();
        }
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    /// <summary>MAUI's main-thread helpers, on MauiLite's UI loop.</summary>
    public static class MainThread
    {
        public static bool IsMainThread => UiLoop.Current.IsUiThread;

        public static void BeginInvokeOnMainThread(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (IsMainThread)
                action();
            else
                UiLoop.Current.Post(action);
        }

        public static Task InvokeOnMainThreadAsync(Action action)
        {
            if (IsMainThread)
            {
                action();
                return Task.CompletedTask;
            }
            return Microsoft.Maui.Dispatching.DispatcherExtensions.DispatchAsync(UiLoop.Current, action);
        }

        public static Task<T> InvokeOnMainThreadAsync<T>(Func<T> func) =>
            IsMainThread ? Task.FromResult(func()) : Microsoft.Maui.Dispatching.DispatcherExtensions.DispatchAsync(UiLoop.Current, func);

        public static Task InvokeOnMainThreadAsync(Func<Task> funcTask) =>
            IsMainThread ? funcTask() : Microsoft.Maui.Dispatching.DispatcherExtensions.DispatchAsync(UiLoop.Current, funcTask);

        public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> funcTask) =>
            IsMainThread ? funcTask() : Microsoft.Maui.Dispatching.DispatcherExtensions.DispatchAsync(UiLoop.Current, funcTask);

        public static Task<SynchronizationContext> GetMainThreadSynchronizationContextAsync() =>
            InvokeOnMainThreadAsync(() => SynchronizationContext.Current!);
    }
}
