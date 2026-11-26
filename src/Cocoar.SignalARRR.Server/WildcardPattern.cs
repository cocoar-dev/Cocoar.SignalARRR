namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Matches a client attribute value against a filter: the whole value, ignoring case, where
    /// <c>*</c> stands for any run of characters and <c>?</c> for exactly one. Every other character,
    /// <c>.</c> and <c>[</c> included, means itself — this is not a regular expression.
    /// </summary>
    /// <remarks>
    /// The same rules as the <c>Match</c> extension of Cocoar.Reflectensions this replaces, which the
    /// server no longer depends on (its reflection helpers do not run under NativeAOT).
    /// </remarks>
    internal static class WildcardPattern {

        public static bool IsMatch(string value, string pattern) {
            int v = 0, p = 0, starPattern = -1, starValue = 0;

            while (v < value.Length) {
                if (p < pattern.Length && (pattern[p] == '?' || SameIgnoringCase(pattern[p], value[v]))) {
                    v++;
                    p++;
                } else if (p < pattern.Length && pattern[p] == '*') {
                    starPattern = p++;
                    starValue = v;
                } else if (starPattern >= 0) {
                    // Let the last '*' take one more character and try again from there.
                    p = starPattern + 1;
                    v = ++starValue;
                } else {
                    return false;
                }
            }

            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static bool SameIgnoringCase(char a, char b) =>
            a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
    }
}
