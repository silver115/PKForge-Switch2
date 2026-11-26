using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Controls;

public interface IGestureRecognizer
{
}

public class GestureRecognizer : Element, IGestureRecognizer
{
}

[Flags]
public enum ButtonsMask { Primary = 1 << 0, Secondary = 1 << 1 }

public class TappedEventArgs(object? parameter) : EventArgs
{
    internal Point? Position { get; init; }

    public object? Parameter { get; } = parameter;

    public ButtonsMask Buttons => ButtonsMask.Primary;

    /// <summary>Where the tap landed, relative to <paramref name="relativeTo"/> (or the window).</summary>
    public Point? GetPosition(Element? relativeTo)
    {
        if (Position is not { } position)
            return null;
        if (relativeTo is VisualElement visual)
            return new Point(position.X - visual.WindowBounds.X, position.Y - visual.WindowBounds.Y);
        return position;
    }
}

/// <summary>MAUI's tap gesture: one or more quick taps on its view.</summary>
public sealed class TapGestureRecognizer : GestureRecognizer
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(TapGestureRecognizer), null);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(TapGestureRecognizer), null);
    public static readonly BindableProperty NumberOfTapsRequiredProperty = BindableProperty.Create(nameof(NumberOfTapsRequired), typeof(int), typeof(TapGestureRecognizer), 1);

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public int NumberOfTapsRequired { get => (int)GetValue(NumberOfTapsRequiredProperty)!; set => SetValue(NumberOfTapsRequiredProperty, value); }
    public ButtonsMask Buttons { get; set; } = ButtonsMask.Primary;

    public event EventHandler<TappedEventArgs>? Tapped;

    internal void SendTapped(View sender, Point position)
    {
        if (Command is { } command && command.CanExecute(CommandParameter))
            command.Execute(CommandParameter);
        Tapped?.Invoke(sender, new TappedEventArgs(CommandParameter) { Position = position });
    }
}

public class PointerEventArgs : EventArgs
{
    internal Point Position { get; init; }

    public ButtonsMask Button => ButtonsMask.Primary;

    /// <summary>Where the pointer is, relative to <paramref name="relativeTo"/> (or the window).</summary>
    public Point? GetPosition(Element? relativeTo) =>
        relativeTo is VisualElement visual ? new Point(Position.X - visual.WindowBounds.X, Position.Y - visual.WindowBounds.Y) : Position;
}

/// <summary>MAUI's pointer gesture: press, move and release on its view, from the touch screen.</summary>
public sealed class PointerGestureRecognizer : GestureRecognizer
{
    public event EventHandler<PointerEventArgs>? PointerPressed;
    public event EventHandler<PointerEventArgs>? PointerMoved;
    public event EventHandler<PointerEventArgs>? PointerReleased;

    internal void SendPressed(View sender, Point at) => PointerPressed?.Invoke(sender, new PointerEventArgs { Position = at });

    internal void SendMoved(View sender, Point at) => PointerMoved?.Invoke(sender, new PointerEventArgs { Position = at });

    internal void SendReleased(View sender, Point at) => PointerReleased?.Invoke(sender, new PointerEventArgs { Position = at });
}

public partial class View
{
    ObservableCollection<IGestureRecognizer>? _gestureRecognizers;

    public IList<IGestureRecognizer> GestureRecognizers => _gestureRecognizers ??= [];

    internal bool HasGestures => _gestureRecognizers is { Count: > 0 };
}
