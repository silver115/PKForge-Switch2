using System.Runtime.InteropServices;

namespace PKForge.Switch2.Platform;

/// <summary>Heap use as the native host reports it: the whole heap (malloc's part plus Mono's), and what each side holds.</summary>
public readonly record struct HostMemory(ulong HeapTotal, ulong MallocUsed, ulong MonoUsed)
{
    /// <summary>The share of the heap in no allocation, 1 − (malloc + Mono) / heap; 0 for an empty heap.
    /// malloc's figure is an upper bound (mimalloc counts only whole free units), so this is a lower bound.</summary>
    public double FreeFraction => HeapTotal == 0 ? 0 : 1 - (double)(MallocUsed + MonoUsed) / HeapTotal;
}

/// <summary>Versions, memory and time from the native host's <c>pkf_*</c> functions (switch2/pkfhost/pkf_host.h).</summary>
public static unsafe class NativeSystem
{
    private const string Lib = "pkfhost";

    [DllImport(Lib)] private static extern long pkf_versions(byte* firmware, int length, uint* kaihenProtocol);
    [DllImport(Lib)] private static extern void pkf_memory(ulong* heapTotal, ulong* mallocUsed, ulong* monoUsed);
    [DllImport(Lib)] private static extern ulong pkf_ms_since_main();
    [DllImport(Lib)] private static extern ulong pkf_core_mask();
    [DllImport(Lib)] private static extern long pkf_pin_thread(int core);
    [DllImport(Lib)] private static extern long pkf_live_log();

    /// <summary>The CPU cores this process may run threads on (bit n = core n).</summary>
    public static ulong CoreMask() => pkf_core_mask();

    /// <summary>
    /// When <c>switch2/toolchain/netload.py --log</c> started the app, its log also goes live to that PC. Called once
    /// Mono runs; false when the app was not started that way or the PC is not listening.
    /// </summary>
    public static bool StartLiveLog() => pkf_live_log() == 0;

    /// <summary>Moves the calling thread to <paramref name="core"/>; libnx starts every thread on the same core.</summary>
    public static void PinThread(int core)
    {
        var result = pkf_pin_thread(core);
        if (result < 0) throw new IOException($"Could not move a thread to core {core}: result 0x{-result:X}");
    }

    /// <summary>The firmware's display version ("unknown" if the console does not say) and kaiHEN's protocol
    /// version (0 when kaiHEN does not answer).</summary>
    public static (string Firmware, uint KaihenProtocol) Versions()
    {
        const int Length = 64;
        var firmware = stackalloc byte[Length];
        uint protocol;
        pkf_versions(firmware, Length, &protocol);
        return (Marshal.PtrToStringUTF8((nint)firmware) ?? "", protocol);
    }

    public static HostMemory Memory()
    {
        ulong total, malloc, mono;
        pkf_memory(&total, &malloc, &mono);
        return new HostMemory(total, malloc, mono);
    }

    /// <summary>Time since the host process started (a constructor in the host runs before main).</summary>
    public static TimeSpan SinceMain() => TimeSpan.FromMilliseconds(pkf_ms_since_main());
}
