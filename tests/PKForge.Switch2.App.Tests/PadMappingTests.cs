using PKForge.App.Services;
using PKForge.MauiLite;
using PKForge.Switch2;

namespace PKForge.Switch2.App.Tests;

// The Switch's buttons reach PKForge's GamepadRouter as Android's key handling (MainActivity) sends them.
public class PadMappingTests
{
    sealed class Screen(bool handles = true, bool pagesWithShoulders = false) : IPadPagingHandler, IPadReleaseHandler
    {
        public readonly List<PadButton> Pressed = [];
        public readonly List<PadButton> Released = [];

        public bool ShouldersRepeat => pagesWithShoulders;

        public bool OnPadButton(PadButton button)
        {
            Pressed.Add(button);
            return handles;
        }

        public void OnPadReleased(PadButton button) => Released.Add(button);
    }

    sealed class StickScreen : IPadHandler, IPadStickHandler
    {
        public (float, float, float, float) Last;

        public bool OnPadButton(PadButton button) => false;

        public void OnStick(float leftX, float leftY, float rightX, float rightY) => Last = (leftX, leftY, rightX, rightY);
    }

    static (PadMapping Mapping, GamepadRouter Router, List<string> Backs) Make(IPadHandler screen)
    {
        var router = new GamepadRouter();
        router.Push(screen);
        var backs = new List<string>();
        return (new PadMapping(() => router, () => backs.Add("back")), router, backs);
    }

    static PadState Held(ulong buttons) => new(buttons, 0, 0, 0, 0);

    static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    [Fact]
    public void Press_A_DispatchesOnce()
    {
        var screen = new Screen();
        var (pad, _, _) = Make(screen);

        pad.Update(Held(NpadButton.A), Ms(0));
        pad.Update(Held(NpadButton.A), Ms(16));
        pad.Update(Held(NpadButton.A), Ms(33));

        Assert.Equal([PadButton.A], screen.Pressed);
    }

    [Fact]
    public void HeldDown_RepeatsAfter320ThenEvery80()
    {
        var screen = new Screen();
        var (pad, _, _) = Make(screen);

        pad.Update(Held(NpadButton.Down), Ms(0));
        pad.Update(Held(NpadButton.Down), Ms(319));
        Assert.Single(screen.Pressed);
        pad.Update(Held(NpadButton.Down), Ms(320));
        Assert.Equal(2, screen.Pressed.Count);
        pad.Update(Held(NpadButton.Down), Ms(400));
        Assert.Equal(3, screen.Pressed.Count);
    }

    [Fact]
    public void B_Unhandled_GoesToSystemBack()
    {
        var (unhandled, _, backs) = Make(new Screen(handles: false));
        unhandled.Update(Held(NpadButton.B), Ms(0));
        Assert.Equal(["back"], backs);

        var (handled, _, none) = Make(new Screen(handles: true));
        handled.Update(Held(NpadButton.B), Ms(0));
        Assert.Empty(none);
    }

    [Fact]
    public void Shoulder_RepeatsOnlyWhenTopPagesWithShoulders()
    {
        var still = new Screen(pagesWithShoulders: false);
        var (noRepeat, _, _) = Make(still);
        noRepeat.Update(Held(NpadButton.R), Ms(0));
        noRepeat.Update(Held(NpadButton.R), Ms(1000));
        Assert.Equal([PadButton.R], still.Pressed);

        var paging = new Screen(pagesWithShoulders: true);
        var (repeat, _, _) = Make(paging);
        repeat.Update(Held(NpadButton.ZR), Ms(0));
        repeat.Update(Held(NpadButton.ZR), Ms(380));
        repeat.Update(Held(NpadButton.ZR), Ms(520));
        Assert.Equal([PadButton.R, PadButton.R, PadButton.R], paging.Pressed);
    }

    [Fact]
    public void Stick_DeadZoneRescaled()
    {
        var screen = new StickScreen();
        var (pad, _, _) = Make(screen);

        pad.Update(new PadState(0, 0.25f, 0.625f, 1f, -1f), Ms(0));

        Assert.Equal((0f, 0.5f, 1f, -1f), screen.Last);
    }

    [Fact]
    public void LeftStick_StepsLikeTheDpad_OnlyWhenNoScreenReadsSticks()
    {
        var screen = new Screen();
        var (pad, router, _) = Make(screen);
        pad.Update(Held(NpadButton.StickLDown), Ms(0));
        pad.Update(Held(0), Ms(16));
        Assert.Equal([PadButton.Down], screen.Pressed);

        router.Push(new StickScreen());
        pad.Update(Held(NpadButton.StickLDown), Ms(32));
        Assert.Equal([PadButton.Down], screen.Pressed);
    }

    [Fact]
    public void Release_DispatchesRelease()
    {
        var screen = new Screen();
        var (pad, _, _) = Make(screen);

        pad.Update(Held(NpadButton.A), Ms(0));
        pad.Update(Held(0), Ms(16));

        Assert.Equal([PadButton.A], screen.Released);
    }
}
