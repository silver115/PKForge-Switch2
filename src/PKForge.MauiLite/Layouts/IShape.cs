namespace Microsoft.Maui.Graphics;

// MAUI declares IShape in its Core assembly, in the Graphics namespace.
public interface IShape
{
    PathF PathForBounds(Rect bounds);
}
