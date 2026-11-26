using SkiaSharp;

namespace PKForge.MauiLite;

/// <summary>
/// Shows frames on a thread of its own. <see cref="Submit"/> copies the frame and returns, so the UI thread starts
/// the next frame while this one is handed to the display (the console's framebufferEnd takes about 10 ms, then the
/// display refresh). One frame may wait behind the one being shown; a third waits in <see cref="Submit"/>, so the UI
/// thread still keeps the display's pace.
/// </summary>
public sealed class FramePresenter : IDisposable
{
    readonly Action<SKPixmap> _present;
    readonly SKBitmap[] _buffers;
    readonly Thread _thread;
    readonly object _gate = new();  // a Monitor, not a Lock: Submit and Run wait on it
    int _pending = -1;
    int _showing = -1;
    bool _stopping;

    /// <param name="present">Shows a frame; called on the presenter's thread only.</param>
    public FramePresenter(SKImageInfo info, Action<SKPixmap> present)
    {
        ArgumentNullException.ThrowIfNull(present);
        _present = present;
        _buffers = [new SKBitmap(info), new SKBitmap(info)];
        _thread = new Thread(Run) { IsBackground = true, Name = "present" };
        _thread.Start();
    }

    /// <summary>Copies <paramref name="frame"/> for showing, after waiting for the frame before it to be taken.</summary>
    public void Submit(SKPixmap frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int free;
        lock (_gate)
        {
            while (_pending >= 0 && !_stopping)
                Monitor.Wait(_gate);
            if (_stopping)
                return;
            free = _showing == 0 ? 1 : 0;
        }
        // Only Submit makes a buffer pending, so the presenter does not touch this one while it is copied.
        var buffer = _buffers[free];
        frame.ReadPixels(buffer.Info, buffer.GetPixels(), buffer.RowBytes, 0, 0);
        lock (_gate)
        {
            _pending = free;
            Monitor.PulseAll(_gate);
        }
    }

    void Run()
    {
        while (true)
        {
            int index;
            lock (_gate)
            {
                while (_pending < 0 && !_stopping)
                    Monitor.Wait(_gate);
                if (_stopping)
                    return;
                index = _showing = _pending;
                _pending = -1;
                Monitor.PulseAll(_gate);
            }
            try
            {
                using var pixmap = _buffers[index].PeekPixels();
                _present(pixmap);
            }
            catch (Exception error)
            {
                UiErrors.Report(error, "present");
            }
            lock (_gate)
            {
                _showing = -1;
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>Stops after the frame being shown, if any: the display can then be closed.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
        _thread.Join();
        foreach (var buffer in _buffers)
            buffer.Dispose();
    }
}
