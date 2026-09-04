using System;
using System.Text;

namespace TestShared {
    /// <summary>
    /// A deterministic string of an exact length, so a test can push far more data than a query
    /// string carries and the other side can verify it arrived intact rather than merely sized.
    /// </summary>
    public static class SizedTestPayload {
        public static string Create(int size) {
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            var builder = new StringBuilder(size);
            builder.Append("sized:").Append(size).Append(':');
            if (builder.Length > size) {
                builder.Length = size;
            }

            const string filler = "abcdefghijklmnopqrstuvwxyz0123456789";
            while (builder.Length < size) {
                builder.Append(filler, 0, Math.Min(filler.Length, size - builder.Length));
            }

            return builder.ToString();
        }

        public static bool IsValid(string? value, int size) {
            return value != null && value.Length == size && string.Equals(value, Create(size), StringComparison.Ordinal);
        }
    }
}
