namespace Hatch.Cli.Tests;

/// <summary>
/// Numbers as somebody reads them off a terminal.
/// </summary>
public sealed class FormatTests
{
    [Fact]
    public void Spent_SaysUnpricedForNull_AndNeverReadsACheapSessionAsFree()
    {
        Assert.Equal("unpriced", Format.Spent(null));
        Assert.Equal("$1.5", Format.Spent(1.5m));
        Assert.Equal("$0.0012", Format.Spent(0.0012m));
    }

    [Theory]
    [InlineData(25_769_803_776L, "24GB")]
    [InlineData(38_654_705_664L, "36GB")]
    [InlineData(1_610_612_736L, "1.5GB")]
    [InlineData(1_073_741_824L, "1GB")]
    [InlineData(0L, "0GB")]
    public void Bytes_IsBinaryGigabytesToOneDecimal_WithATrailingPointZeroDropped(long bytes, string expected)
    {
        Assert.Equal(expected, Format.Bytes(bytes));
    }
}
