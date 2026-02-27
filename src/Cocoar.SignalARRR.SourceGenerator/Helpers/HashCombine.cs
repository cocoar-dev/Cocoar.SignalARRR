namespace Cocoar.SignalARRR.SourceGenerator.Helpers;

/// <summary>
/// Simple hash combining helper for netstandard2.0 compatibility (System.HashCode is unavailable).
/// </summary>
internal static class HashCombine
{
    public static int Combine(int h1, int h2)
    {
        unchecked
        {
            return ((h1 << 5) + h1) ^ h2;
        }
    }

    public static int Of<T>(T? value) => value?.GetHashCode() ?? 0;
}
