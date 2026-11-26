// Ported from dotnet/maui 10.0.20, src/Core/src/Layouts/ILayoutManager.cs (MIT licence; see THIRD-PARTY.md).
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Layouts
{
	public interface ILayoutManager
	{
		Size Measure(double widthConstraint, double heightConstraint);

		Size ArrangeChildren(Rect bounds);
	}
}
