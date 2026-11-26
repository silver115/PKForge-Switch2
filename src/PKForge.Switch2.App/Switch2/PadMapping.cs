using PKForge.App.Services;
using PKForge.MauiLite;

namespace PKForge.Switch2;

/// <summary>libnx's <c>HidNpadButton</c> bits, as the host reports held buttons.</summary>
public static class NpadButton
{
    public const ulong A = 1UL << 0;
    public const ulong B = 1UL << 1;
    public const ulong X = 1UL << 2;
    public const ulong Y = 1UL << 3;
    public const ulong L = 1UL << 6;
    public const ulong R = 1UL << 7;
    public const ulong ZL = 1UL << 8;
    public const ulong ZR = 1UL << 9;
    public const ulong Plus = 1UL << 10;
    public const ulong Minus = 1UL << 11;
    public const ulong Left = 1UL << 12;
    public const ulong Up = 1UL << 13;
    public const ulong Right = 1UL << 14;
    public const ulong Down = 1UL << 15;
    public const ulong StickLLeft = 1UL << 16;
    public const ulong StickLUp = 1UL << 17;
    public const ulong StickLRight = 1UL << 18;
    public const ulong StickLDown = 1UL << 19;
}

/// <summary>
/// The controller into PKForge's <see cref="GamepadRouter"/>, as Android's MainActivity sends key and hat events:
/// a press is dispatched once; B that no screen takes is the system back; held directions repeat after 320 ms every
/// 80 ms; held L/R repeat after 380 ms every 140 ms on screens that page with them; the sticks go to a screen that
/// reads them, and otherwise the left stick steps like the D-pad.
/// </summary>
public sealed class PadMapping(Func<GamepadRouter?> router, Action systemBack)
{
    static readonly (ulong Bits, PadButton Button)[] Buttons =
    [
        (NpadButton.A, PadButton.A), (NpadButton.B, PadButton.B), (NpadButton.X, PadButton.X), (NpadButton.Y, PadButton.Y),
        (NpadButton.Plus, PadButton.Start), (NpadButton.Minus, PadButton.Select),
        (NpadButton.L | NpadButton.ZL, PadButton.L), (NpadButton.R | NpadButton.ZR, PadButton.R),
        (NpadButton.Up, PadButton.Up), (NpadButton.Down, PadButton.Down), (NpadButton.Left, PadButton.Left), (NpadButton.Right, PadButton.Right),
    ];

    static readonly (ulong Bits, PadButton Button)[] StickSteps =
    [
        (NpadButton.StickLUp, PadButton.Up), (NpadButton.StickLDown, PadButton.Down),
        (NpadButton.StickLLeft, PadButton.Left), (NpadButton.StickLRight, PadButton.Right),
    ];

    static readonly TimeSpan HatHoldDelay = TimeSpan.FromMilliseconds(320);
    static readonly TimeSpan HatRepeat = TimeSpan.FromMilliseconds(80);
    static readonly TimeSpan ShoulderHoldDelay = TimeSpan.FromMilliseconds(380);
    static readonly TimeSpan ShoulderRepeat = TimeSpan.FromMilliseconds(140);

    readonly HashSet<PadButton> _held = [];
    PadButton? _repeating;
    TimeSpan _nextRepeat;

    public void Update(PadState pad, TimeSpan now)
    {
        var target = router();
        var sticksRead = target?.DispatchStick(DeadZone(pad.LeftX), DeadZone(pad.LeftY), DeadZone(pad.RightX), DeadZone(pad.RightY)) == true;
        var down = new HashSet<PadButton>();
        foreach (var (bits, button) in Buttons)
            if ((pad.Held & bits) != 0)
                down.Add(button);
        if (!sticksRead)
            foreach (var (bits, button) in StickSteps)
                if ((pad.Held & bits) != 0)
                    down.Add(button);

        foreach (var released in _held.Where(button => !down.Contains(button)).ToArray())
        {
            _held.Remove(released);
            if (_repeating == released)
                _repeating = null;
            target?.DispatchRelease(released);
        }
        foreach (var pressed in down.Where(button => !_held.Contains(button)).ToArray())
        {
            _held.Add(pressed);
            // Asked before dispatching: the press itself may open another screen.
            var repeats = IsDirectional(pressed) || (IsShoulder(pressed) && target?.TopPagesWithShoulders == true);
            var handled = target?.Dispatch(pressed) == true;
            if (handled && repeats)
            {
                _repeating = pressed;
                _nextRepeat = now + (IsDirectional(pressed) ? HatHoldDelay : ShoulderHoldDelay);
            }
            else if (!handled && pressed == PadButton.B)
                systemBack();
        }
        if (_repeating is { } held && now >= _nextRepeat)
        {
            if (!IsDirectional(held) && target?.TopPagesWithShoulders != true || target?.Dispatch(held) != true)
                _repeating = null;
            else
                _nextRepeat = now + (IsDirectional(held) ? HatRepeat : ShoulderRepeat);
        }
    }

    /// <summary>Past a 0.25 dead zone (worn sticks drift), rescaled to start at 0, as MainActivity reads stick axes.</summary>
    internal static float DeadZone(float value) =>
        Math.Abs(value) <= 0.25f ? 0 : Math.Sign(value) * (Math.Abs(value) - 0.25f) / 0.75f;

    static bool IsDirectional(PadButton button) => button is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;

    static bool IsShoulder(PadButton button) => button is PadButton.L or PadButton.R;
}
