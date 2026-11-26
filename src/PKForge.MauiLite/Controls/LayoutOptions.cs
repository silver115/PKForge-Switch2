using Microsoft.Maui.Primitives;

namespace Microsoft.Maui.Controls;

/// <summary>MAUI's alignment of a view inside the space its parent gives it.</summary>
public readonly struct LayoutOptions : IEquatable<LayoutOptions>
{
    public static readonly LayoutOptions Start = new(LayoutAlignment.Start, false);
    public static readonly LayoutOptions Center = new(LayoutAlignment.Center, false);
    public static readonly LayoutOptions End = new(LayoutAlignment.End, false);
    public static readonly LayoutOptions Fill = new(LayoutAlignment.Fill, false);
    [Obsolete("The StackLayout expansion options are deprecated; please use a Grid instead.")]
    public static readonly LayoutOptions StartAndExpand = new(LayoutAlignment.Start, true);
    [Obsolete("The StackLayout expansion options are deprecated; please use a Grid instead.")]
    public static readonly LayoutOptions CenterAndExpand = new(LayoutAlignment.Center, true);
    [Obsolete("The StackLayout expansion options are deprecated; please use a Grid instead.")]
    public static readonly LayoutOptions EndAndExpand = new(LayoutAlignment.End, true);
    [Obsolete("The StackLayout expansion options are deprecated; please use a Grid instead.")]
    public static readonly LayoutOptions FillAndExpand = new(LayoutAlignment.Fill, true);

    public LayoutOptions(LayoutAlignment alignment, bool expands)
    {
        Alignment = alignment;
        Expands = expands;
    }

    public LayoutAlignment Alignment { get; }
    public bool Expands { get; }

    public bool Equals(LayoutOptions other) => Alignment == other.Alignment && Expands == other.Expands;
    public override bool Equals(object? obj) => obj is LayoutOptions other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Alignment, Expands);
    public static bool operator ==(LayoutOptions left, LayoutOptions right) => left.Equals(right);
    public static bool operator !=(LayoutOptions left, LayoutOptions right) => !left.Equals(right);
}
