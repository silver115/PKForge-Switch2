using System.Runtime.InteropServices;

namespace PKForge.Switch2.Platform;

/// <summary>One poll of the controller and the touch screen (switch2/pkfhost/pkf_host.h, field for field): touch in frame
/// pixels, buttons pressed since the last poll and the largest raw touch values seen (the phase 0 spike's fields); then
/// held buttons, the sticks (up positive), the raw touch in 1280×720, and whether the system asked the app to close.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct HostInput
{
    public int TouchCount, TouchX, TouchY;
    public ulong ButtonsDown;
    public int Quit;
    public int RawMaxX, RawMaxY;
    public ulong ButtonsHeld;
    public int StickLX, StickLY, StickRX, StickRY;
    public int RawX, RawY;
    public int AppletQuit;
}

/// <summary>The screen and input through the native host's <c>pkf_video_*</c> functions (switch2/pkfhost/pkf_video.c).</summary>
public static unsafe class NativeVideo
{
    private const string Lib = "pkfhost";

    [DllImport(Lib)] private static extern long pkf_video_init(int width, int height);
    [DllImport(Lib)] private static extern long pkf_present(void* rgba, int stride);
    [DllImport(Lib)] private static extern long pkf_poll(HostInput* output);
    [DllImport(Lib)] private static extern void pkf_video_exit();
    [DllImport(Lib)] private static extern void pkf_present_times(ulong* output);

    public static void Open(int width, int height) => Check(pkf_video_init(width, height), "open the screen");

    /// <summary>Shows one RGBA8888 frame; waits for a free buffer, so it paces the caller to the display.</summary>
    public static void Present(nint rgba, int stride) => Check(pkf_present((void*)rgba, stride), "present a frame");

    /// <summary>Average milliseconds per presented frame: waiting for a free buffer, copying the frame, queueing it.</summary>
    public static (double WaitMs, double CopyMs, double QueueMs) PresentTimes()
    {
        var times = stackalloc ulong[4];
        pkf_present_times(times);
        var frames = Math.Max(1, times[3]);
        return (times[0] / 1e6 / frames, times[1] / 1e6 / frames, times[2] / 1e6 / frames);
    }

    public static HostInput Poll()
    {
        HostInput input;
        Check(pkf_poll(&input), "read the input");
        return input;
    }

    public static void Close() => pkf_video_exit();

    private static void Check(long result, string what)
    {
        if (result < 0) throw new IOException($"Could not {what}: result 0x{-result:X}");
    }
}
