using Microsoft.Maui.Controls;
using PKForge.MauiLite;

namespace SkiaSharp.Views.Maui
{
    public enum SKTouchAction { Entered, Pressed, Moved, Released, Cancelled, Exited, WheelChanged }

    public enum SKTouchDeviceType { Touch, Mouse, Pen }

    public enum SKMouseButton { Unknown, Left, Middle, Right }

    public class SKPaintSurfaceEventArgs : EventArgs
    {
        public SKPaintSurfaceEventArgs(SKSurface surface, SKImageInfo info) : this(surface, info, info)
        {
        }

        public SKPaintSurfaceEventArgs(SKSurface surface, SKImageInfo info, SKImageInfo rawInfo)
        {
            Surface = surface;
            Info = info;
            RawInfo = rawInfo;
        }

        public SKSurface Surface { get; }
        public SKImageInfo Info { get; }
        public SKImageInfo RawInfo { get; }
    }

    public class SKTouchEventArgs : EventArgs
    {
        public SKTouchEventArgs(long id, SKTouchAction type, SKPoint location, bool inContact)
            : this(id, type, SKMouseButton.Left, SKTouchDeviceType.Touch, location, inContact)
        {
        }

        public SKTouchEventArgs(long id, SKTouchAction type, SKMouseButton mouseButton, SKTouchDeviceType deviceType, SKPoint location, bool inContact)
            : this(id, type, mouseButton, deviceType, location, inContact, 0, 1)
        {
        }

        public SKTouchEventArgs(long id, SKTouchAction type, SKMouseButton mouseButton, SKTouchDeviceType deviceType, SKPoint location, bool inContact,
            int wheelDelta, float pressure)
        {
            Id = id;
            ActionType = type;
            MouseButton = mouseButton;
            DeviceType = deviceType;
            Location = location;
            InContact = inContact;
            WheelDelta = wheelDelta;
            Pressure = pressure;
        }

        public bool Handled { get; set; }
        public long Id { get; }
        public SKTouchAction ActionType { get; }
        public SKTouchDeviceType DeviceType { get; }
        public SKMouseButton MouseButton { get; }
        public SKPoint Location { get; }
        public bool InContact { get; }
        public int WheelDelta { get; }
        public float Pressure { get; }
    }
}

namespace SkiaSharp.Views.Maui.Controls
{
    /// <summary>
    /// SkiaSharp's canvas view. It paints into its own surface only after <see cref="InvalidateSurface"/> or a size change,
    /// and draws that cached picture on every other frame.
    /// </summary>
    public class SKCanvasView : View
    {
        public static readonly BindableProperty IgnorePixelScalingProperty = BindableProperty.Create(nameof(IgnorePixelScaling), typeof(bool), typeof(SKCanvasView), false,
            propertyChanged: (bindable, _, _) => ((SKCanvasView)bindable).InvalidateSurface());
        public static readonly BindableProperty EnableTouchEventsProperty = BindableProperty.Create(nameof(EnableTouchEvents), typeof(bool), typeof(SKCanvasView), false);

        SKSurface? _surface;
        SKImage? _picture;
        SKSizeI _surfaceSize;
        bool _needsPaint = true;

        public bool IgnorePixelScaling { get => (bool)GetValue(IgnorePixelScalingProperty)!; set => SetValue(IgnorePixelScalingProperty, value); }
        public bool EnableTouchEvents { get => (bool)GetValue(EnableTouchEventsProperty)!; set => SetValue(EnableTouchEventsProperty, value); }

        /// <summary>The surface size in pixels (in units when <see cref="IgnorePixelScaling"/> is set).</summary>
        public SKSize CanvasSize { get; private protected set; }

        /// <summary>Set by <see cref="InvalidateSurface"/>: the next frame paints again.</summary>
        private protected bool NeedsPaint { get => _needsPaint; set => _needsPaint = value; }

        EventHandler<SKPaintSurfaceEventArgs>? _paintSurface;
        string? _painter;

        public event EventHandler<SKPaintSurfaceEventArgs>? PaintSurface
        {
            add { _paintSurface += value; _painter = null; }
            remove { _paintSurface -= value; _painter = null; }
        }

        public event EventHandler<SKTouchEventArgs>? Touch;

        // A frame's profile names the paint by its handler: "paint BoxBrowserPage.Paint".
        internal override string PrepareName => _painter ??= "paint " + (_paintSurface?.GetInvocationList() is [var first, ..] ? Painter(first) : "SKCanvasView");

        private protected static string Painter(Delegate handler)
        {
            // A wrapper (PKForge's PaintGuard.Wrap) is a lambda whose closure holds the handler it calls.
            for (var depth = 0; depth < 4 && handler.Target is { } closure && closure.GetType().Name.StartsWith('<'); depth++)
            {
                if (closure.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                        .Select(field => field.GetValue(closure)).OfType<Delegate>().FirstOrDefault() is not { } inner)
                    break;
                handler = inner;
            }
            // A lambda is named by the method it is written in (<Method>b__0_0), on the type outside its closure class.
            var method = handler.Method;
            var type = method.DeclaringType;
            while (type is { IsNested: true } && type.Name.StartsWith('<'))
                type = type.DeclaringType;
            var name = method.Name.StartsWith('<') && method.Name.IndexOf('>') is > 1 and var end ? method.Name[1..end] : method.Name;
            return $"{type?.Name}.{name}";
        }

        public void InvalidateSurface()
        {
            _needsPaint = true;
            Invalidate();
        }

        protected virtual void OnPaintSurface(SKPaintSurfaceEventArgs e) => _paintSurface?.Invoke(this, e);

        protected virtual void OnTouch(SKTouchEventArgs e) => Touch?.Invoke(this, e);

        /// <summary>Delivers a touch event (MauiLite's input layer calls this).</summary>
        internal void SendTouch(SKTouchEventArgs e) => OnTouch(e);

        // The app's PaintSurface handlers run here, on the UI thread, as on Android.
        internal override void Prepare(SKSize size, float scale)
        {
            var width = (int)Math.Ceiling(size.Width);
            var height = (int)Math.Ceiling(size.Height);
            if (width <= 0 || height <= 0)
                return;
            if (_surface is null || _surfaceSize != new SKSizeI(width, height))
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
                _surfaceSize = new SKSizeI(width, height);
                _needsPaint = true;
            }
            if (_needsPaint)
                Paint(width, height, scale);
        }

        internal override void Draw(SKCanvas canvas, SKSize size, float scale)
        {
            base.Draw(canvas, size, scale);
            if (_picture is null || size.Width <= 0 || size.Height <= 0)
                return;
            if (PaintsOwnOpacity && Opacity < 1)
            {
                using var faded = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Opacity * 255)) };
                canvas.DrawImage(_picture, 0, 0, faded);
            }
            else
                canvas.DrawImage(_picture, 0, 0);
        }

        // Its picture alone, with no background or shadow under it, can take the opacity as paint alpha.
        internal override bool PaintsOwnOpacity =>
            Shadow is null && Background is not { IsEmpty: false } && (BackgroundColor is null || BackgroundColor.Alpha == 0);

        void Paint(int width, int height, float scale)
        {
            _needsPaint = false;
            var raw = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var info = raw;
            var surfaceCanvas = _surface!.Canvas;
            surfaceCanvas.Clear(SKColors.Transparent);
            surfaceCanvas.Save();
            if (IgnorePixelScaling)
            {
                // As SkiaSharp's Android view: the painter sees units, and the canvas scales them to pixels.
                info = raw.WithSize((int)(width / scale), (int)(height / scale));
                surfaceCanvas.Scale(scale);
            }
            CanvasSize = new SKSize(info.Width, info.Height);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                OnPaintSurface(new SKPaintSurfaceEventArgs(_surface, info, raw));
            }
            catch (Exception error)
            {
                UiErrors.Report(error, $"{GetType().Name}.PaintSurface");
            }
            finally
            {
                PKForge.MauiLite.PaintClock.Add(System.Diagnostics.Stopwatch.GetTimestamp() - started);
            }
            surfaceCanvas.Restore();
            surfaceCanvas.Flush();
            _picture?.Dispose();
            _picture = _surface.Snapshot();
        }
    }
}

namespace SkiaSharp.Views.Maui
{
    /// <summary>
    /// The render target a GL view's paint event carries. MauiLite draws GL views on the CPU, so it is the surface's size
    /// in pixels, all PKForge reads from it (ParkCanvas): SkiaSharp's GRBackendRenderTarget needs Skia's GPU code, which
    /// the console's Skia leaves out.
    /// </summary>
    public sealed record SKGLRenderTarget(int Width, int Height);

    /// <summary>
    /// A GL view's surface in MauiLite: <see cref="Canvas"/> records what the painter draws, and the frame replays it
    /// strip by strip on the drawing threads (see SKGLView). Code that needs a real SKSurface (PKForge's PaintGuard
    /// clears it after a failed paint) gets a 1x1 one through the conversion.
    /// </summary>
    public sealed class SKGLSurface(SKCanvas canvas, SKSurface fallback)
    {
        public SKCanvas Canvas { get; } = canvas;

        public static implicit operator SKSurface(SKGLSurface surface) => surface.Fallback;

        private SKSurface Fallback { get; } = fallback;
    }

    public class SKPaintGLSurfaceEventArgs : EventArgs
    {
        public SKPaintGLSurfaceEventArgs(SKGLSurface surface, SKGLRenderTarget renderTarget, GRSurfaceOrigin origin, SKImageInfo info, SKImageInfo rawInfo)
        {
            Surface = surface;
            BackendRenderTarget = renderTarget;
            Origin = origin;
            Info = info;
            RawInfo = rawInfo;
        }

        public SKGLSurface Surface { get; }
        public SKGLRenderTarget BackendRenderTarget { get; }
        public GRSurfaceOrigin Origin { get; }
        public SKImageInfo Info { get; }
        public SKImageInfo RawInfo { get; }
        public SKColorType ColorType => Info.ColorType;
    }
}

namespace SkiaSharp.Views.Maui.Controls
{
    /// <summary>
    /// SkiaSharp's GL view, drawn by MauiLite on the CPU. Its painter draws on a recording canvas, and the frame replays
    /// the recording strip by strip on the drawing threads: the Poképark scales its pixel-art world up to the whole
    /// screen each frame, which on one core cost 29 ms of the console's 37 ms frame (2026-11-27). The paint event
    /// carries GL-shaped arguments (a render target of the surface's size). With <see cref="HasRenderLoop"/> it
    /// repaints on every frame.
    /// </summary>
    public class SKGLView : SKCanvasView
    {
        SKPicture? _recording;
        SKSizeI _recordedSize;
        readonly SKSurface _fallback = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul));
        string? _painter;

        public static readonly BindableProperty HasRenderLoopProperty = BindableProperty.Create(nameof(HasRenderLoop), typeof(bool), typeof(SKGLView), false,
            propertyChanged: (bindable, _, now) =>
            {
                if ((bool)now)
                    ((SKGLView)bindable).InvalidateSurface();
            });

        public bool HasRenderLoop { get => (bool)GetValue(HasRenderLoopProperty)!; set => SetValue(HasRenderLoopProperty, value); }

        EventHandler<SKPaintGLSurfaceEventArgs>? _paintGLSurface;

        public new event EventHandler<SKPaintGLSurfaceEventArgs>? PaintSurface
        {
            add { _paintGLSurface += value; _painter = null; }
            remove { _paintGLSurface -= value; _painter = null; }
        }

        internal override string PrepareName =>
            _painter ??= "paint " + (_paintGLSurface?.GetInvocationList() is [var first, ..] ? Painter(first) : "SKGLView");

        protected virtual void OnPaintSurface(SKPaintGLSurfaceEventArgs e) => _paintGLSurface?.Invoke(this, e);

        // The painter runs here, on the UI thread, and records; the drawing threads replay it in Draw.
        internal override void Prepare(SKSize size, float scale)
        {
            var width = (int)Math.Ceiling(size.Width);
            var height = (int)Math.Ceiling(size.Height);
            if (width <= 0 || height <= 0 || (!NeedsPaint && _recordedSize == new SKSizeI(width, height)))
                return;
            NeedsPaint = false;
            _recordedSize = new SKSizeI(width, height);
            var raw = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var info = raw;
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(SKRect.Create(width, height));
            if (IgnorePixelScaling)
            {
                info = raw.WithSize((int)(width / scale), (int)(height / scale));
                canvas.Scale(scale);
            }
            CanvasSize = new SKSize(info.Width, info.Height);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                OnPaintSurface(new SKPaintGLSurfaceEventArgs(new SKGLSurface(canvas, _fallback), new SKGLRenderTarget(info.Width, info.Height),
                    GRSurfaceOrigin.TopLeft, info, raw));
            }
            catch (Exception error)
            {
                UiErrors.Report(error, $"{GetType().Name}.PaintSurface");
            }
            finally
            {
                PKForge.MauiLite.PaintClock.Add(System.Diagnostics.Stopwatch.GetTimestamp() - started);
            }
            _recording?.Dispose();
            _recording = recorder.EndRecording();
            if (HasRenderLoop)
                InvalidateSurface();
        }

        // Each strip replays the recording within the view's bounds: a Clear in it stays inside the view.
        internal override void Draw(SKCanvas canvas, SKSize size, float scale)
        {
            base.Draw(canvas, size, scale);
            if (_recording is null || size.Width <= 0 || size.Height <= 0)
                return;
            canvas.Save();
            canvas.ClipRect(SKRect.Create(size));
            if (PaintsOwnOpacity && Opacity < 1)
            {
                using var faded = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Opacity * 255)) };
                canvas.DrawPicture(_recording, faded);
            }
            else
                canvas.DrawPicture(_recording);
            canvas.Restore();
        }
    }
}
