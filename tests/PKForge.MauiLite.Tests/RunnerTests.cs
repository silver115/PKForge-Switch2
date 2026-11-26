using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using PKForge.MauiLite;
using SkiaSharp;

namespace PKForge.MauiLite.Tests;

public sealed class RunnerTests : IDisposable
{
    public void Dispose() => Application.Current = null;

    sealed class FakeBackend(float scale = 1.5f) : IHostBackend
    {
        public int Presents, Waits;
        public SKSizeI PixelSize => new(1920, 1080);
        public float Scale => scale;
        public TimeSpan Now { get; private set; }
        public bool QuitRequested => false;
        public IReadOnlyList<PointerEvent> PollPointers() => [];
        public PadState PollPad() => default;

        public void Present(SKPixmap frame)
        {
            Presents++;
            Now += TimeSpan.FromMilliseconds(16);
        }

        public void WaitForNextFrame()
        {
            Waits++;
            Now += TimeSpan.FromMilliseconds(16);
        }
    }

    public sealed class HomePage : ContentPage
    {
        public readonly BoxView Box = new() { Color = Colors.White };

        public HomePage() => Content = Box;
    }

    public sealed class TestApp : Application
    {
        public static HomePage? Home;

        protected override Window CreateWindow(IActivationState? activationState) => new(Home = new HomePage());
    }

    static LiteRunner Start(FakeBackend backend)
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<TestApp>();
        return new LiteRunner(builder.Build(), backend);
    }

    [Fact]
    public void Runner_IdleFrame_DoesNotPresent()
    {
        var backend = new FakeBackend();
        var runner = Start(backend);

        runner.RunFrame();
        runner.RunFrame();

        Assert.Equal(1, backend.Presents);
        Assert.Equal(1, backend.Waits);
    }

    // The window in units is the screen in pixels over the scale: 853⅓ by 480 at the Thor's 2.25.
    [Fact]
    public void Runner_WindowIsPixelsOverScale()
    {
        var runner = Start(new FakeBackend(2.25f));
        runner.RunFrame();

        Assert.Equal(1920 / 2.25, Application.Current!.Windows[0].Width, 6);
        Assert.Equal(480, Application.Current.Windows[0].Height, 6);
        Assert.Equal(1920 / 2.25, TestApp.Home!.Width, 6);
    }

    public sealed class PaintApp : Application
    {
        protected override Window CreateWindow(IActivationState? activationState)
        {
            var canvas = new SkiaSharp.Views.Maui.Controls.SKCanvasView();
            canvas.PaintSurface += (_, _) => Thread.Sleep(20);
            return new(new ContentPage { Content = canvas });
        }
    }

    // Task 12 splits a slow frame into the app's PaintSurface handlers, the rest of drawing, and presenting.
    [Fact]
    public void Runner_RecordsPaintSurfaceTimeApart()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<PaintApp>();
        var runner = new LiteRunner(builder.Build(), new FakeBackend());

        // On a busy machine the first slow frames count as the page opening (at most 2 s): run until one is a frame.
        while (!runner.Frames.Pages.ContainsKey("ContentPage"))
            runner.RunFrame();

        var parts = runner.Frames.Pages["ContentPage"].AverageParts;
        Assert.InRange(parts.PaintMs, 15, parts.RenderMs);
    }

    [Fact]
    public void Runner_RecordsFramesPerPage()
    {
        var backend = new FakeBackend();
        var runner = Start(backend);

        runner.RunFrame();
        TestApp.Home!.Box.Color = Colors.Red;
        runner.RunFrame();

        Assert.Equal("HomePage", runner.CurrentPageName);
        Assert.Equal(2, runner.Frames.Pages["HomePage"].Frames);
    }
    // A page's slow first frames (building it, first paints) are its open time, not part of its frame rate.
    [Fact]
    public void Runner_TimesPageOpeningApartFromFrames()
    {
        var slowPaints = 2;
        var canvas = new SkiaSharp.Views.Maui.Controls.SKCanvasView();
        canvas.PaintSurface += (_, _) =>
        {
            if (slowPaints-- > 0)
                Thread.Sleep(40);
        };
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<PaintApp>();
        var runner = new LiteRunner(builder.Build(), new FakeBackend());
        ((ContentPage)Application.Current!.Windows[0].Page!).Content = canvas;

        for (var frame = 0; frame < 4; frame++)
        {
            runner.RunFrame();
            canvas.InvalidateSurface();
        }

        var open = runner.Frames.Opens["ContentPage"];
        Assert.Equal(1, open.Count);
        Assert.InRange(open.AverageMs, 80, 1000);
        Assert.Equal(2, runner.Frames.Pages["ContentPage"].Frames);
    }
    // An opening splits into the same parts as a frame, summed over its frames: here its two 40 ms paints.
    [Fact]
    public void Runner_RecordsOpeningParts()
    {
        var slowPaints = 2;
        var canvas = new SkiaSharp.Views.Maui.Controls.SKCanvasView();
        canvas.PaintSurface += (_, _) =>
        {
            if (slowPaints-- > 0)
                Thread.Sleep(40);
        };
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<PaintApp>();
        var runner = new LiteRunner(builder.Build(), new FakeBackend());
        ((ContentPage)Application.Current!.Windows[0].Page!).Content = canvas;

        // On a busy machine more frames can be slow (the opening ends within 2 s): run until it ends.
        while (!runner.Frames.Opens.ContainsKey("ContentPage"))
        {
            runner.RunFrame();
            canvas.InvalidateSurface();
        }

        var parts = runner.Frames.Opens["ContentPage"].AverageParts;
        Assert.InRange(parts.PaintMs, 80, parts.RenderMs);
    }
    // An opening page waiting for its data draws nothing for a while; the UI loop's work in those frames is still its.
    [Fact]
    public void Runner_OpeningCountsFramesThatDrawNothing()
    {
        var slowPaints = 1;
        var canvas = new SkiaSharp.Views.Maui.Controls.SKCanvasView();
        canvas.PaintSurface += (_, _) =>
        {
            if (slowPaints-- > 0)
                Thread.Sleep(40);
        };
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<PaintApp>();
        var runner = new LiteRunner(builder.Build(), new FakeBackend());
        ((ContentPage)Application.Current!.Windows[0].Page!).Content = canvas;

        runner.RunFrame();
        UiLoop.Current.Post(() => Thread.Sleep(60));
        runner.RunFrame();
        while (!runner.Frames.Opens.ContainsKey("ContentPage"))
        {
            canvas.InvalidateSurface();
            runner.RunFrame();
        }

        Assert.True(runner.Frames.Opens["ContentPage"].AverageParts.LoopMs >= 60);
    }
    // A page that goes still after its slow first frame opened by that frame: it is recorded when the next page shows.
    [Fact]
    public void Runner_StillPageOpensByItsLastDrawnFrame()
    {
        var canvas = new SkiaSharp.Views.Maui.Controls.SKCanvasView();
        canvas.PaintSurface += (_, _) => Thread.Sleep(40);
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<PaintApp>();
        var runner = new LiteRunner(builder.Build(), new FakeBackend());
        var window = Application.Current!.Windows[0];
        ((ContentPage)window.Page!).Content = canvas;

        for (var frame = 0; frame < 3; frame++)
            runner.RunFrame();
        window.Page = new OtherPage();
        runner.RunFrame();

        var open = runner.Frames.Opens["ContentPage"];
        Assert.Equal(1, open.Count);
        Assert.InRange(open.AverageMs, 40, 1000);
    }

    sealed class OtherPage : ContentPage;
    // The console shows frames on a thread of its own: Submit returns at once, a second frame waits behind the one
    // being shown, and a third waits in Submit, so the UI thread keeps the display's pace.
    [Fact]
    public async Task Presenter_ShowsFramesInOrderOnItsOwnThread()
    {
        var info = new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        var shown = new List<(SKColor Color, int Thread)>();
        using var hold = new ManualResetEventSlim();
        using var presenter = new FramePresenter(info, frame =>
        {
            lock (shown)
                shown.Add((frame.GetPixelColor(0, 0), Environment.CurrentManagedThreadId));
            hold.Wait();
        });
        void Submit(SKColor color)
        {
            using var frame = new SKBitmap(info);
            frame.Erase(color);
            using var pixels = frame.PeekPixels();
            presenter.Submit(pixels);
        }
        int Shown() { lock (shown) return shown.Count; }

        Submit(SKColors.Red);
        SpinWait.SpinUntil(() => Shown() == 1, 5000);
        Submit(SKColors.Lime);
        var third = Task.Run(() => Submit(SKColors.Blue));
        Assert.NotSame(third, await Task.WhenAny(third, Task.Delay(100)));
        hold.Set();
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        SpinWait.SpinUntil(() => Shown() == 3, 5000);

        lock (shown)
        {
            Assert.Equal([SKColors.Red, SKColors.Lime, SKColors.Blue], shown.Select(frame => frame.Color));
            Assert.DoesNotContain(Environment.CurrentManagedThreadId, shown.Select(frame => frame.Thread));
        }
    }
}
