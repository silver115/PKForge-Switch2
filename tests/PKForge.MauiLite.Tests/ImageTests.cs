using PKForge.MauiLite;
using SkiaSharp;

namespace PKForge.MauiLite.Tests;

public sealed class ImageTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), $"mauilite-image-{Guid.NewGuid():N}.png");

    // An image's natural size is its pixels over the density: this test uses 1.5.
    public ImageTests() => PKForge.MauiLite.Rendering.Display.Density = 1.5f;

    public void Dispose() => File.Delete(_file);

    void WritePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Red);
        using var output = File.Create(_file);
        bitmap.Encode(output, SKEncodedImageFormat.Png, 100);
    }

    // Home's cartridge art: an image file shown after it was hidden, set while its tile was already on screen.
    [Fact]
    public void Image_ShownLater_DrawsItsBitmap()
    {
        WritePng(256, 256);
        var image = new Image { Aspect = Aspect.AspectFit, IsVisible = false, Margin = new Thickness(3) };
        var cell = new Grid { WidthRequest = 57, HeightRequest = 58, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, Children = { image } };
        var root = new Grid { BackgroundColor = Colors.Black, Children = { cell } };
        using var bitmap = new SKBitmap(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new Renderer(1.5f);
        LayoutPass.Run(root, new Size(1280, 720));
        renderer.Render(root, canvas, full: true);

        image.Source = ImageSource.FromFile(_file);
        image.IsVisible = true;
        LayoutPass.Run(root, new Size(1280, 720));
        renderer.Render(root, canvas);
        canvas.Flush();

        Assert.Equal(SKColors.Red, bitmap.GetPixel(43, 43));  // the cell's centre: 28.5 units at 1.5 pixels per unit
    }

    // PKForge caches art and hands out the path while another caller may still be writing the file; Android's image
    // loader fails quietly then. MauiLite tries again shortly and only reports a file that never becomes readable.
    [Fact]
    public void Image_FileStillBeingWritten_LoadsOnceItIsDone()
    {
        WritePng(30, 15);
        var reported = 0;
        void Record(Exception error, string where) => reported++;
        var image = new Image { Source = ImageSource.FromFile(_file), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        var root = new Grid { image };
        UiErrors.Reported += Record;
        try
        {
            using (new FileStream(_file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                LayoutPass.Run(root, new Size(1280, 720));
            Assert.Equal(0, image.Width);

            UiLoop.Current.RunOnce(UiLoop.Current.Now + TimeSpan.FromMilliseconds(300));
            LayoutPass.Run(root, new Size(1280, 720));
        }
        finally
        {
            UiErrors.Reported -= Record;
        }

        Assert.Equal(0, reported);
        Assert.Equal(20, image.Width);  // 30 pixels at 1.5 pixels per unit
    }
}
