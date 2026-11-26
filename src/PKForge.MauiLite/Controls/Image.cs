using Microsoft.Maui.Graphics;
using PKForge.MauiLite;
using PKForge.MauiLite.Rendering;
using SkiaSharp;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's image view. A bitmap's natural size in units is its pixel size divided by the density, as on Android.</summary>
public class Image : View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(Image), null,
        propertyChanged: (bindable, _, _) => ((Image)bindable).Reload()).Measured();
    public static readonly BindableProperty AspectProperty = BindableProperty.Create(nameof(Aspect), typeof(Aspect), typeof(Image), Aspect.AspectFit).Measured();
    public static readonly BindableProperty IsOpaqueProperty = BindableProperty.Create(nameof(IsOpaque), typeof(bool), typeof(Image), false);

    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Aspect Aspect { get => (Aspect)GetValue(AspectProperty)!; set => SetValue(AspectProperty, value); }
    public bool IsOpaque { get => (bool)GetValue(IsOpaqueProperty)!; set => SetValue(IsOpaqueProperty, value); }
    public bool IsLoading => false;

    // A file another caller is still writing (PKForge's art cache) is tried again this often, this many times.
    static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    const int Attempts = 4;

    SKImage? _image;
    bool _loaded;
    int _failures;

    void Reload()
    {
        _image?.Dispose();
        _image = null;
        _loaded = false;
        _failures = 0;
    }

    SKImage? Bitmap()
    {
        if (!_loaded)
        {
            _loaded = true;
            try
            {
                _image = Source?.Load();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException && ++_failures < Attempts)
            {
                var source = Source;
                UiLoop.Current.DispatchDelayed(RetryDelay, () =>
                {
                    if (!ReferenceEquals(source, Source))
                        return;
                    _loaded = false;
                    InvalidateLayout();
                });
            }
            catch (Exception error)
            {
                UiErrors.Report(error, "image source");
            }
        }
        return _image;
    }

    internal override Size MeasureContent(double availableWidth, double availableHeight)
    {
        if (Bitmap() is not { } image)
            return Size.Zero;
        var width = image.Width / Display.Density;
        var height = image.Height / Display.Density;
        if (Aspect == Aspect.Fill)
            return new Size(Math.Min(width, availableWidth), Math.Min(height, availableHeight));
        // Scaled down as a whole to fit, keeping its shape (Android's adjustViewBounds, which MAUI sets).
        var fit = Math.Min(1, Math.Min(availableWidth / width, availableHeight / height));
        return new Size(width * fit, height * fit);
    }

    internal override void Prepare(SKSize size, float scale) => Bitmap();

    internal override void Draw(SKCanvas canvas, SKSize size, float scale)
    {
        base.Draw(canvas, size, scale);
        if (Bitmap() is not { } image || size.Width <= 0 || size.Height <= 0)
            return;
        var source = SKRect.Create(image.Width, image.Height);
        var area = SKRect.Create(size);
        var target = Aspect switch
        {
            Aspect.Fill => area,
            Aspect.Center => SKRect.Create((size.Width - image.Width) / 2, (size.Height - image.Height) / 2, image.Width, image.Height),
            _ => Fitted(image, size, Aspect == Aspect.AspectFill ? Math.Max(size.Width / image.Width, size.Height / image.Height)
                                                                 : Math.Min(size.Width / image.Width, size.Height / image.Height)),
        };
        canvas.Save();
        canvas.ClipRect(area);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, source, target, new SKSamplingOptions(SKFilterMode.Linear), paint);
        canvas.Restore();
    }

    static SKRect Fitted(SKImage image, SKSize size, float factor)
    {
        var width = image.Width * factor;
        var height = image.Height * factor;
        return SKRect.Create((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }
}

/// <summary>MAUI's image source.</summary>
public abstract class ImageSource : Element
{
#nullable disable // MAUI declares these without nullable annotations
    public static ImageSource FromStream(Func<Stream> stream) => new StreamImageSource { Stream = _ => Task.FromResult(stream()) };

    public static ImageSource FromStream(Func<CancellationToken, Task<Stream>> stream) => new StreamImageSource { Stream = stream };

    public static ImageSource FromFile(string file) => new FileImageSource { File = file };

    public static implicit operator ImageSource(string source) => FromFile(source);
#nullable restore

    public virtual bool IsEmpty => false;

    internal abstract SKImage? Load();

    private protected static SKImage? Decode(Stream stream)
    {
        using var data = SKData.Create(stream);
        return data is null ? null : SKImage.FromEncodedData(data);
    }
}

public sealed class StreamImageSource : ImageSource
{
    public Func<CancellationToken, Task<Stream>>? Stream { get; set; }

    public override bool IsEmpty => Stream is null;

    internal override SKImage? Load()
    {
        if (Stream is null)
            return null;
        using var stream = Stream(CancellationToken.None).GetAwaiter().GetResult();
        return stream is null ? null : Decode(stream);
    }
}

/// <summary>A file: an absolute path, or else a file in the app package (MAUI resolves bare names to the app's images).</summary>
public sealed class FileImageSource : ImageSource
{
    public string? File { get; set; }

    public override bool IsEmpty => string.IsNullOrEmpty(File);

    internal override SKImage? Load()
    {
        if (string.IsNullOrEmpty(File))
            return null;
        using var stream = Path.IsPathRooted(File)
            ? new FileStream(File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
            : LitePackage.Open(File);
        // A file cut short (still being written) decodes to nothing: the image tries again.
        return Decode(stream) ?? throw new InvalidDataException($"'{File}' is not a readable image yet.");
    }
}
