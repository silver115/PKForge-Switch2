// Ported from dotnet/maui 10.0.20, src/Core/src/Primitives/Dimension.cs (MIT licence; see THIRD-PARTY.md).
namespace Microsoft.Maui.Primitives
{
	public static class Dimension
	{
		public const double Minimum = 0;
		public const double Unset = double.NaN;
		public const double Maximum = double.PositiveInfinity;

		public static bool IsExplicitSet(double value)
		{
			return !double.IsNaN(value);
		}

		public static bool IsMaximumSet(double value)
		{
			return !double.IsPositiveInfinity(value);
		}

		public static bool IsMinimumSet(double value)
		{
			return !double.IsNaN(value);
		}

		public static double ResolveMinimum(double value)
		{
			if (IsMinimumSet(value))
			{
				return value;
			}

			return Minimum;
		}
	}
}
