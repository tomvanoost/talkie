using Talkie.Shared;

namespace Talkie.Tests;

public class NumberFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1K")]
    [InlineData(1_500, "1.5K")]
    [InlineData(1_999, "1.9K")]
    [InlineData(25_000, "25K")]
    [InlineData(1_000_000, "1M")]
    [InlineData(2_340_000, "2.3M")]
    [InlineData(1_000_000_000, "1B")]
    [InlineData(1_000_000_000_000, "1T")]
    [InlineData(1_000_000_000_000_000, "1Q")]
    [InlineData(-1_500, "-1.5K")]
    public void Compact_formats_with_suffixes(long value, string expected) =>
        Assert.Equal(expected, NumberFormatter.Compact(value));

    [Fact]
    public void Money_adds_dollar_sign() => Assert.Equal("$10K", NumberFormatter.Money(10_000));

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(125, "2m 5s")]
    [InlineData(3_700, "1h 1m")]
    [InlineData(90_000, "1d 1h 0m")]
    public void Duration_is_human_readable(long seconds, string expected) =>
        Assert.Equal(expected, NumberFormatter.Duration(seconds));
}
