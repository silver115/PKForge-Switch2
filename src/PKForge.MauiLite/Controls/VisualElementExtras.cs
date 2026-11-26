using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Microsoft.Maui
{
    /// <summary>MAUI's platform handler. MauiLite has no platform views: a loaded element has a handler with none.</summary>
    public interface IElementHandler
    {
        object? PlatformView { get; }
        void UpdateValue(string property);
        void Invoke(string command, object? args = null);
        void DisconnectHandler();
    }

    public interface IViewHandler : IElementHandler
    {
    }

    sealed class LiteHandler : IViewHandler
    {
        public static readonly LiteHandler Instance = new();

        public object? PlatformView => null;
        public void UpdateValue(string property) { }
        public void Invoke(string command, object? args = null) { }
        public void DisconnectHandler() { }
    }
}

namespace Microsoft.Maui.Controls
{
    public abstract partial class VisualElement
    {
        public static readonly BindableProperty TranslationXProperty = BindableProperty.Create(nameof(TranslationX), typeof(double), typeof(VisualElement), 0d);
        public static readonly BindableProperty TranslationYProperty = BindableProperty.Create(nameof(TranslationY), typeof(double), typeof(VisualElement), 0d);
        public static readonly BindableProperty RotationProperty = BindableProperty.Create(nameof(Rotation), typeof(double), typeof(VisualElement), 0d);
        public static readonly BindableProperty ScaleProperty = BindableProperty.Create(nameof(Scale), typeof(double), typeof(VisualElement), 1d);
        public static readonly BindableProperty ScaleXProperty = BindableProperty.Create(nameof(ScaleX), typeof(double), typeof(VisualElement), 1d);
        public static readonly BindableProperty ScaleYProperty = BindableProperty.Create(nameof(ScaleY), typeof(double), typeof(VisualElement), 1d);
        public static readonly BindableProperty AnchorXProperty = BindableProperty.Create(nameof(AnchorX), typeof(double), typeof(VisualElement), 0.5d);
        public static readonly BindableProperty AnchorYProperty = BindableProperty.Create(nameof(AnchorY), typeof(double), typeof(VisualElement), 0.5d);

        public double TranslationX { get => (double)GetValue(TranslationXProperty); set => SetValue(TranslationXProperty, value); }
        public double TranslationY { get => (double)GetValue(TranslationYProperty); set => SetValue(TranslationYProperty, value); }
        public double Rotation { get => (double)GetValue(RotationProperty); set => SetValue(RotationProperty, value); }
        public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
        public double ScaleX { get => (double)GetValue(ScaleXProperty); set => SetValue(ScaleXProperty, value); }
        public double ScaleY { get => (double)GetValue(ScaleYProperty); set => SetValue(ScaleYProperty, value); }
        public double AnchorX { get => (double)GetValue(AnchorXProperty); set => SetValue(AnchorXProperty, value); }
        public double AnchorY { get => (double)GetValue(AnchorYProperty); set => SetValue(AnchorYProperty, value); }

        /// <summary>True when any render transform is set (the renderer applies them around the anchor).</summary>
        internal bool HasTransform => TranslationX != 0 || TranslationY != 0 || Rotation != 0 || Scale != 1 || ScaleX != 1 || ScaleY != 1;

        /// <summary>The element's transform in its own pixel space: translate, then rotate and scale around the anchor.</summary>
        internal SkiaSharp.SKMatrix TransformMatrix(SkiaSharp.SKSize size, float scale)
        {
            var pivotX = (float)AnchorX * size.Width;
            var pivotY = (float)AnchorY * size.Height;
            var matrix = SkiaSharp.SKMatrix.CreateTranslation((float)TranslationX * scale, (float)TranslationY * scale);
            matrix = matrix.PreConcat(SkiaSharp.SKMatrix.CreateRotationDegrees((float)Rotation, pivotX, pivotY));
            return matrix.PreConcat(SkiaSharp.SKMatrix.CreateScale((float)(Scale * ScaleX), (float)(Scale * ScaleY), pivotX, pivotY));
        }

        // Loading: an element is loaded while it is part of a window's tree. Its "handler" exists while it is loaded,
        // which is what MAUI code tests (Handler is null) to know whether a view is on screen.
        public bool IsLoaded => IsLoadedCore;

        public IViewHandler? Handler => IsLoadedCore ? LiteHandler.Instance : null;

        public event EventHandler? Loaded;
        public event EventHandler? Unloaded;
        public event EventHandler? HandlerChanged;
        public event EventHandler<HandlerChangingEventArgs>? HandlerChanging;

        /// <summary>The window this element is in, if any.</summary>
        public Window? Window
        {
            get
            {
                for (Element? e = Parent; e is not null; e = e.Parent)
                    if (e is Window window)
                        return window;
                return null;
            }
        }

        internal override void OnLoadedChanged(bool loaded)
        {
            HandlerChanging?.Invoke(this, new HandlerChangingEventArgs());
            HandlerChanged?.Invoke(this, EventArgs.Empty);
            (loaded ? Loaded : Unloaded)?.Invoke(this, EventArgs.Empty);
        }

        ObservableCollection<TriggerBase>? _triggers;

        public IList<TriggerBase> Triggers
        {
            get
            {
                if (_triggers is null)
                {
                    _triggers = [];
                    _triggers.CollectionChanged += OnTriggersChanged;
                }
                return _triggers;
            }
        }

        void OnTriggersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var trigger in e.OldItems?.OfType<TriggerBase>() ?? [])
                trigger.Detach();
            foreach (var trigger in e.NewItems?.OfType<TriggerBase>() ?? [])
                trigger.Attach(this);
        }
    }

    public sealed class HandlerChangingEventArgs : EventArgs
    {
    }

    public partial class Element
    {
        bool _loaded;

        internal bool IsLoadedCore => _loaded;

        internal virtual void OnLoadedChanged(bool loaded)
        {
        }

        /// <summary>Recomputes whether this element is in a window's tree, and passes a change on to its children.</summary>
        internal void UpdateLoaded()
        {
            var loaded = Parent is Window || Parent is { IsLoadedCore: true };
            if (loaded == _loaded)
                return;
            _loaded = loaded;
            OnLoadedChanged(loaded);
            foreach (var child in InheritingChildren)
                if (child is Element element)
                    element.UpdateLoaded();
        }
    }
}
