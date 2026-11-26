using Microsoft.Maui.Controls.Shapes;
using PKForge.MauiLite;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.MauiLite.Tests;

public class RenderTests
{
    const float Scale = 1.5f;

    static RenderTests()
    {
        var fonts = Path.Combine(AppContext.BaseDirectory, "Fonts");
        FontRegistry.Register("NDS12.ttf", "PixelUI", file => File.OpenRead(Path.Combine(fonts, file)));
        FontRegistry.Register("MPLUSRounded1c-Regular.ttf", "Rounded", file => File.OpenRead(Path.Combine(fonts, file)));
    }

    sealed class Frame : IDisposable
    {
        public readonly SKBitmap Bitmap = new(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul);
        public readonly SKCanvas Canvas;
        public readonly Renderer Renderer = new(Scale);

        public Frame() => Canvas = new SKCanvas(Bitmap);

        public IReadOnlyList<SKRectI> Render(VisualElement root, bool full = false)
        {
            LayoutPass.Run(root, new Size(1280, 720));
            var drawn = Renderer.Render(root, Canvas, full);
            Canvas.Flush();
            return drawn;
        }

        public SKColor this[int x, int y] => Bitmap.GetPixel(x, y);

        public void Dispose()
        {
            Canvas.Dispose();
            Bitmap.Dispose();
        }
    }

    static Grid BlackScreen(params View[] children)
    {
        var grid = new Grid { BackgroundColor = Colors.Black };
        foreach (var child in children)
            grid.Add(child);
        return grid;
    }

    static T TopLeft<T>(T view, double width, double height, double left = 0) where T : View
    {
        view.WidthRequest = width;
        view.HeightRequest = height;
        view.HorizontalOptions = LayoutOptions.Start;
        view.VerticalOptions = LayoutOptions.Start;
        view.Margin = new Thickness(left, 0, 0, 0);
        return view;
    }

    [Fact]
    public void CanvasView_PaintsOnceUntilInvalidated()
    {
        var paints = 0;
        var canvas = new SKCanvasView();
        canvas.PaintSurface += (_, _) => paints++;
        using var frame = new Frame();
        var root = BlackScreen(canvas);

        frame.Render(root, full: true);
        frame.Render(root);
        Assert.Equal(1, paints);

        canvas.InvalidateSurface();
        frame.Render(root);
        Assert.Equal(2, paints);
    }

    [Fact]
    public void CanvasView_SurfaceIsBoundsTimesScale()
    {
        SKImageInfo? info = null;
        var canvas = TopLeft(new SKCanvasView(), 100, 50);
        canvas.PaintSurface += (_, e) => info = e.Info;
        using var frame = new Frame();

        frame.Render(BlackScreen(canvas), full: true);

        Assert.Equal(150, info?.Width);
        Assert.Equal(75, info?.Height);
    }

    [Fact]
    public void Render_RedrawsOnlyTheChangedElement()
    {
        var first = TopLeft(new BoxView { Color = Colors.White }, 100, 100);
        var second = TopLeft(new BoxView { Color = Colors.White }, 100, 100, left: 200);
        var root = BlackScreen(first, second);
        using var frame = new Frame();
        frame.Render(root, full: true);

        second.Color = Colors.Red;
        var drawn = frame.Render(root);

        Assert.Equal([new SKRectI(300, 0, 450, 150)], drawn);
        Assert.Equal(SKColors.Red, frame[375, 75]);
    }

    // Home's missing cartridge art: a half-transparent element left the canvas one save deep (Save and SaveLayer, one
    // Restore), so later frames drew with its stale clip and translation and skipped what changed.
    [Fact]
    public void HalfTransparentElement_LeavesLaterFramesDrawingRight()
    {
        var faded = TopLeft(new BoxView { Color = Colors.White, Opacity = 0.5 }, 100, 100);
        var changing = TopLeft(new BoxView { Color = Colors.White }, 100, 100, left: 400);
        var root = BlackScreen(faded, changing);
        using var frame = new Frame();
        frame.Render(root, full: true);

        changing.Color = Colors.Red;
        frame.Render(root);

        Assert.Equal(SKColors.Red, frame[675, 75]);
        Assert.Equal(1, frame.Canvas.SaveCount);
    }

    [Fact]
    public void Profile_CountsEachDrawLayerAndRedrawMark()
    {
        var plain = TopLeft(new BoxView { Color = Colors.White }, 100, 100);
        var faded = TopLeft(new BoxView { Color = Colors.White, Opacity = 0.5 }, 100, 100, left: 200);
        using var frame = new Frame();
        var profile = frame.Renderer.Profile = new DrawProfile();

        frame.Render(BlackScreen(plain, faded), full: true);

        var draws = profile.SlowestDraws(10).ToDictionary(draw => draw.Part, draw => draw.Count);
        Assert.Equal(2, draws["BoxView"]);
        Assert.Equal(1, draws["layer BoxView"]);
        // Two new 150-pixel squares at 1.5 pixels per unit, each marked once.
        Assert.Contains(profile.BiggestDirt(10), dirt => dirt == ("BoxView", 2, 2 * 150 * 150));
    }

    // A canvas's paint is named by its PaintSurface handler, inside a wrapper like PKForge's PaintGuard.Wrap too, and a
    // lambda by the method it is written in.
    [Fact]
    public void Profile_NamesEachPaintSurfaceHandler()
    {
        var plain = TopLeft(new SKCanvasView(), 100, 100);
        plain.PaintSurface += PaintWhite;
        var wrapped = TopLeft(new SKCanvasView(), 100, 100, left: 200);
        wrapped.PaintSurface += Guard("the wrapped one", PaintWhite);
        var lambda = TopLeft(new SKCanvasView(), 100, 100, left: 400);
        lambda.PaintSurface += (_, e) => e.Surface.Canvas.Clear(SKColors.Red);
        using var frame = new Frame();
        var profile = frame.Renderer.Profile = new DrawProfile();

        frame.Render(BlackScreen(plain, wrapped, lambda), full: true);

        var draws = profile.SlowestDraws(20).ToDictionary(draw => draw.Part, draw => draw.Count);
        Assert.Equal(2, draws["paint RenderTests.PaintWhite"]);
        Assert.Equal(1, draws["paint RenderTests.Profile_NamesEachPaintSurfaceHandler"]);
    }

    static void PaintWhite(object? sender, SKPaintSurfaceEventArgs e) => e.Surface.Canvas.Clear(SKColors.White);

    static EventHandler<SKPaintSurfaceEventArgs> Guard(string area, EventHandler<SKPaintSurfaceEventArgs> paint) =>
        (sender, args) =>
        {
            try { paint(sender, args); }
            catch (InvalidOperationException) when (area.Length > 0) { }
        };

    // A blur is costly on the console's CPU (about 3 ms a shadow on Home): it is made once per shape and reused.
    [Fact]
    public void Shadow_IsBlurredOnce_UntilItsShapeChanges()
    {
        var box = TopLeft(new BoxView
        {
            Color = Colors.White,
            Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Red), Offset = new Point(20, 20), Radius = 0 },
        }, 100, 100);
        var root = BlackScreen(box);
        using var frame = new Frame();

        frame.Render(root, full: true);
        frame.Render(root, full: true);

        Assert.Equal(SKColors.Red, frame[160, 160]); // the shadow, 30 pixels down and right of the 150-pixel box
        Assert.Equal(SKColors.White, frame[75, 75]);
        Assert.Equal(1, frame.Renderer.ShadowsMade);

        box.WidthRequest = 120;
        frame.Render(root, full: true);

        Assert.Equal(2, frame.Renderer.ShadowsMade);
        Assert.Equal(SKColors.Red, frame[190, 160]);
    }

    // A half-transparent canvas view draws its picture with that alpha: a layer as big as the redrawn area cost about
    // 10 ms each on the console.
    [Fact]
    public void HalfTransparentCanvasView_BlendsWithoutALayer()
    {
        var canvas = TopLeft(new SKCanvasView { Opacity = 0.5 }, 100, 100);
        canvas.PaintSurface += (_, e) => e.Surface.Canvas.Clear(SKColors.White);
        using var frame = new Frame();
        var profile = frame.Renderer.Profile = new DrawProfile();

        frame.Render(BlackScreen(canvas), full: true);

        Assert.InRange(frame[75, 75].Red, 127, 129);
        Assert.DoesNotContain(profile.SlowestDraws(20), draw => draw.Part == "layer SKCanvasView");
    }

    // A layer covers only what the element and its children drew, and that includes a child moved outside it.
    [Fact]
    public void HalfTransparentLayout_StillShowsAChildOutsideIt()
    {
        var child = new BoxView
        {
            Color = Colors.White, WidthRequest = 50, HeightRequest = 50, TranslationX = 150,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        };
        var faded = TopLeft(new Grid { Opacity = 0.5, Children = { child } }, 100, 100);
        using var frame = new Frame();

        frame.Render(BlackScreen(faded), full: true);

        Assert.InRange(frame[262, 37].Red, 127, 129); // 150 units right: pixels 225 to 300
    }

    [Fact]
    public void ZIndex_DrawsHigherOnTop_AndFollowsChanges()
    {
        var red = TopLeft(new BoxView { Color = Colors.Red, ZIndex = 1 }, 100, 100);
        var blue = TopLeft(new BoxView { Color = Colors.Blue }, 100, 100);
        var root = BlackScreen(red, blue);
        using var frame = new Frame();

        frame.Render(root, full: true);
        Assert.Equal(SKColors.Red, frame[75, 75]);

        blue.ZIndex = 2;
        frame.Render(root);
        Assert.Equal(SKColors.Blue, frame[75, 75]);
    }

    static Label Wrapping(string text) => new()
    {
        Text = text, FontFamily = "PixelUI", FontSize = 16, LineBreakMode = LineBreakMode.WordWrap,
    };

    // The status toast cut its text off on the console: its maximum width did not narrow what its label was measured
    // in, so the label measured one line, then wrapped to two when drawn at the toast's width. Android's measure spec
    // (MAUI's CreateMeasureSpec) gives the content the maximum when it is narrower than the space offered.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumWidth_NarrowsWhatTheContentIsMeasuredIn(bool grid)
    {
        var label = Wrapping("A message long enough to wrap onto more than one line");
        View holder = grid
            ? new Grid { Children = { label }, MaximumWidthRequest = 120 }
            : new Border { Content = label, StrokeThickness = 0, MaximumWidthRequest = 120 };
        holder.HorizontalOptions = LayoutOptions.Start;
        holder.VerticalOptions = LayoutOptions.Start;
        var line = new Label { Text = "A", FontFamily = "PixelUI", FontSize = 16, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End };

        LayoutPass.Run(BlackScreen(holder, line), new Size(1280, 720));

        Assert.True(holder.Width <= 120, $"width {holder.Width}");
        Assert.True(holder.Height >= 2 * line.Height - 0.5, $"height {holder.Height}, one line {line.Height}");
    }

    [Fact]
    public void Label_DrawsInTextColorInsideItsBounds()
    {
        var label = new Label
        {
            Text = "WWWW", TextColor = Colors.Red, FontFamily = "PixelUI", FontSize = 32,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start,
        };
        using var frame = new Frame();

        frame.Render(BlackScreen(label), full: true);

        var inside = new SKRectI(0, 0, (int)Math.Ceiling(label.Width * Scale), (int)Math.Ceiling(label.Height * Scale));
        Assert.True(inside.Width > 0 && inside.Height > 0);
        int redInside = 0, redOutside = 0;
        for (var y = 0; y < 300; y++)
            for (var x = 0; x < 600; x++)
                if (frame[x, y].Red > 128 && frame[x, y].Green < 64)
                    if (inside.Contains(x, y)) redInside++; else redOutside++;
        Assert.True(redInside > 50, $"red pixels inside: {redInside}");
        Assert.Equal(0, redOutside);
    }

    [Fact]
    public void Border_RoundRectangleClipsCorners()
    {
        var border = TopLeft(new Border
        {
            BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 20 },
        }, 100, 100);
        using var frame = new Frame();

        frame.Render(BlackScreen(border), full: true);

        Assert.Equal(SKColors.Black, frame[2, 2]);
        Assert.Equal(SKColors.White, frame[75, 75]);
    }

    [Fact]
    public void Opacity_Zero_DrawsNothing()
    {
        var box = TopLeft(new BoxView { Color = Colors.White, Opacity = 0 }, 100, 100);
        using var frame = new Frame();

        frame.Render(BlackScreen(box), full: true);

        Assert.Equal(SKColors.Black, frame[75, 75]);
    }

    [Fact]
    public void PaintSurface_Throwing_IsReportedAndFrameCompletes()
    {
        var reported = new List<string>();
        void Record(Exception error, string where) => reported.Add(where);
        var canvas = TopLeft(new SKCanvasView(), 100, 100);
        canvas.PaintSurface += (_, _) => throw new InvalidOperationException("painter bug");
        var box = TopLeft(new BoxView { Color = Colors.White }, 100, 100, left: 200);
        using var frame = new Frame();
        UiErrors.Reported += Record;
        try
        {
            frame.Render(BlackScreen(canvas, box), full: true);
        }
        finally
        {
            UiErrors.Reported -= Record;
        }

        Assert.Single(reported);
        Assert.Equal(SKColors.White, frame[375, 75]);
    }

    [Fact]
    public void FontRegistry_MissingGlyphFallsBackToRounded()
    {
        const int kanji = '漢'; // NDS12 has 799 characters and no kanji; M PLUS Rounded has them
        Assert.False(FontRegistry.FaceFor("PixelUI", FontAttributes.None, 'A').ContainsGlyph(kanji));

        var face = FontRegistry.FaceFor("PixelUI", FontAttributes.None, kanji);

        Assert.Same(FontRegistry.FaceFor("Rounded", FontAttributes.None, 'A'), face);
    }
    // The console composes in bands on every core. Each strip of rows must draw what one thread draws with the same rows
    // clipped (Skia's anti-aliasing in a clipped layer can differ from an unclipped draw's, as any partial redraw's
    // does), and the app's PaintSurface handlers stay on the UI thread.
    [Fact]
    public void Bands_DrawWhatOneThreadDrawsStripByStrip()
    {
        var paintThreads = new HashSet<int>();
        (Grid Root, Label Label) Scene()
        {
            var canvas = TopLeft(new SKCanvasView(), 300, 300, left: 500);
            canvas.PaintSurface += (_, e) =>
            {
                paintThreads.Add(Environment.CurrentManagedThreadId);
                using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
                e.Surface.Canvas.DrawCircle(e.Info.Width / 2f, e.Info.Height / 2f, e.Info.Width / 2.5f, paint);
            };
            var label = new Label { Text = "Bands of one frame\non every core", FontFamily = "PixelUI", FontSize = 30, TextColor = Colors.White };
            var border = TopLeft(new Border
            {
                Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop { Color = Colors.Navy, Offset = 0 }, new GradientStop { Color = Colors.Teal, Offset = 1 } },
                    new Point(0, 0), new Point(1, 1)),
                Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 3, StrokeShape = new RoundRectangle { CornerRadius = 24 },
                Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Offset = new Point(6, 6), Radius = 12, Opacity = 0.8f },
                Content = label,
            }, 420, 400, left: 40);
            return (BlackScreen(border, TopLeft(new BoxView { Color = Colors.Red, Opacity = 0.5, CornerRadius = 30 }, 400, 500, left: 300), canvas), label);
        }
        static SKSurface NewFrame() => SKSurface.Create(new SKImageInfo(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var bands = new DrawBands([0, 1, 2, 3], _ => { });
        var banded = Scene();
        var renderer = new Renderer(Scale) { Bands = bands };
        using var frame = NewFrame();
        // Each strip again on this thread, from its own copy of the scene: a renderer draws a change only once.
        var strips = bands.Count * Renderer.StripsPerBand;
        var copies = Enumerable.Range(0, strips).Select(_ => (Scene: Scene(), Renderer: new Renderer(Scale))).ToList();
        using var expected = NewFrame();

        foreach (var change in new[] { false, true })
        {
            if (change)
                foreach (var label in copies.Select(copy => copy.Scene.Label).Append(banded.Label))
                    label.Text = "A small change";
            LayoutPass.Run(banded.Root, new Size(1280, 720));
            var areas = renderer.Render(banded.Root, frame, full: !change);
            var (top, bottom) = (areas.Min(area => area.Top), areas.Max(area => area.Bottom));
            var rows = (bottom - top + strips - 1) / strips;
            foreach (var (copy, strip) in copies.Select((copy, strip) => (copy, strip)))
            {
                LayoutPass.Run(copy.Scene.Root, new Size(1280, 720));
                expected.Canvas.Save();
                expected.Canvas.ClipRect(SKRect.Create(0, top + strip * rows, 1920, rows));
                copy.Renderer.Render(copy.Scene.Root, expected.Canvas, full: !change);
                expected.Canvas.Restore();
            }

            using var want = expected.PeekPixels();
            using var got = frame.PeekPixels();
            Assert.True(want.GetPixelSpan().SequenceEqual(got.GetPixelSpan()), $"banded pixels differ (change: {change})");
        }
        Assert.Equal([Environment.CurrentManagedThreadId], paintThreads);
    }
}
