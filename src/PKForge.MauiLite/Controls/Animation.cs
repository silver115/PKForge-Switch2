using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using PKForge.MauiLite;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Microsoft.Maui
{
    /// <summary>MAUI's easing functions (the same formulas).</summary>
    public class Easing
    {
        readonly Func<double, double> _easing;

        public Easing(Func<double, double> easingFunc) => _easing = easingFunc;

        public double Ease(double v) => _easing(v);

        public static implicit operator Easing(Func<double, double> func) => new(func);

        public static readonly Easing Linear = new(x => x);
        public static readonly Easing SinOut = new(x => Math.Sin(x * Math.PI * 0.5f));
        public static readonly Easing SinIn = new(x => 1.0f - Math.Cos(x * Math.PI * 0.5f));
        public static readonly Easing SinInOut = new(x => -Math.Cos(Math.PI * x) / 2.0f + 0.5f);
        public static readonly Easing CubicIn = new(x => x * x * x);
        public static readonly Easing CubicOut = new(x => Math.Pow(x - 1.0f, 3.0f) + 1.0f);
        public static readonly Easing CubicInOut = new(x => x < 0.5f ? Math.Pow(x * 2.0f, 3.0f) / 2.0f : (Math.Pow((x - 1) * 2.0f, 3.0f) + 2.0f) / 2.0f);
        public static readonly Easing BounceOut;
        public static readonly Easing BounceIn;
        public static readonly Easing SpringIn = new(x => x * x * ((1.70158f + 1) * x - 1.70158f));
        public static readonly Easing SpringOut = new(x => (x - 1) * (x - 1) * ((1.70158f + 1) * (x - 1) + 1.70158f) + 1);

        static Easing()
        {
            BounceOut = new Easing(p =>
            {
                if (p < 1 / 2.75f)
                    return 7.5625f * p * p;
                if (p < 2 / 2.75f)
                {
                    p -= 1.5f / 2.75f;
                    return 7.5625f * p * p + .75f;
                }
                if (p < 2.5f / 2.75f)
                {
                    p -= 2.25f / 2.75f;
                    return 7.5625f * p * p + .9375f;
                }
                p -= 2.625f / 2.75f;
                return 7.5625f * p * p + .984375f;
            });
            BounceIn = new Easing(p => 1.0f - BounceOut.Ease(1 - p));
        }
    }
}

namespace PKForge.MauiLite
{
    /// <summary>Runs a value from 0 to 1 over a time on the UI loop, once per frame.</summary>
    static class Tween
    {
        public static Task Run(uint length, Easing? easing, Action<double> step)
        {
            var done = new TaskCompletionSource<bool>();
            var timer = UiLoop.Current.CreateTimer();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            timer.Interval = TimeSpan.Zero;
            timer.Tick += (_, _) =>
            {
                var t = length == 0 ? 1 : Math.Min(1, clock.Elapsed.TotalMilliseconds / length);
                step((easing ?? Easing.Linear).Ease(t));
                if (t < 1)
                    return;
                timer.Stop();
                done.TrySetResult(true);
            };
            timer.Start();
            return done.Task;
        }
    }
}

namespace Microsoft.Maui.Controls
{
    /// <summary>MAUI's progress bar: a thin track filled to <see cref="Progress"/>.</summary>
    public class ProgressBar : View
    {
        public static readonly BindableProperty ProgressProperty = BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressBar), 0d,
            coerceValue: (_, value) => Math.Clamp((double)value, 0, 1));
        public static readonly BindableProperty ProgressColorProperty = BindableProperty.Create(nameof(ProgressColor), typeof(Color), typeof(ProgressBar), null);

        public double Progress { get => (double)GetValue(ProgressProperty)!; set => SetValue(ProgressProperty, value); }
        public Color? ProgressColor { get => (Color?)GetValue(ProgressColorProperty); set => SetValue(ProgressColorProperty, value); }

        public Task<bool> ProgressTo(double value, uint length, Easing easing)
        {
            var from = Progress;
            return Tween.Run(length, easing, t => Progress = from + (value - from) * t).ContinueWith(_ => true, TaskScheduler.Default);
        }

        /// <summary>Android's horizontal progress bar is 4 units tall.</summary>
        internal override Size MeasureContent(double availableWidth, double availableHeight) =>
            new(double.IsInfinity(availableWidth) ? 0 : availableWidth, 4);

        internal override void Draw(SKCanvas canvas, SKSize size, float scale)
        {
            base.Draw(canvas, size, scale);
            var color = (ProgressColor ?? Colors.DodgerBlue).ToSKColor();
            using var paint = new SKPaint { IsAntialias = true, Color = color.WithAlpha((byte)(color.Alpha * 0.3)) };
            canvas.DrawRect(SKRect.Create(size), paint);
            paint.Color = color;
            canvas.DrawRect(SKRect.Create(size.Width * (float)Progress, size.Height), paint);
        }
    }
}
