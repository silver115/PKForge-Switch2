namespace Microsoft.Maui;

// MAUI's own clamp helper, which its grid layout manager calls.
static class NumericExtensions
{
    public static int Clamp(this int self, int min, int max) => Math.Min(max, Math.Max(self, min));

    public static double Clamp(this double self, double min, double max) => Math.Min(max, Math.Max(self, min));
}
