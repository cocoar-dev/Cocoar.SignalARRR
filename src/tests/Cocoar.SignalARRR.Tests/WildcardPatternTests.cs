using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>Attribute filters keep the matching rules they had with Cocoar.Reflectensions.</summary>
public class WildcardPatternTests {

    [Theory]
    [InlineData("writer", "writer", true)]
    [InlineData("Writer", "writer", true)]
    [InlineData("writer", "Writer", true)]
    [InlineData("writer", "writ*", true)]
    [InlineData("writer", "*ter", true)]
    [InlineData("writer", "w?iter", true)]
    [InlineData("writer", "writ", false)]
    [InlineData("xabcx", "abc", false)]
    [InlineData("wri.ter", "wri.ter", true)]
    [InlineData("wriXter", "wri.ter", false)]
    [InlineData("2x1x0", "2.1.0", false)]
    [InlineData("abc", "^abc$", false)]
    [InlineData("a[b]", "a[b]", true)]
    [InlineData("abc", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("", "", true)]
    [InlineData("abc", "a*c*", true)]
    [InlineData("abcbd", "a*b?", true)]
    public void Matches_like_before(string value, string pattern, bool expected) {
        Assert.Equal(expected, WildcardPattern.IsMatch(value, pattern));
    }
}
