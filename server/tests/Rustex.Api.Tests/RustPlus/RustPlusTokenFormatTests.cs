using Rustex.Domain.RustPlus;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

public class RustPlusTokenFormatTests
{
    [Fact]
    public void AlreadySigned_PassesThrough()
    {
        Assert.True(RustPlusTokenFormat.TryNormalize(-2621618, out var token));
        Assert.Equal(-2621618, token);
    }

    [Fact]
    public void UnsignedRendering_NormalizesToSignedValue()
    {
        // 4292345678 is the unsigned 32-bit rendering of -2621618 — some community pairing tools
        // print tokens this way instead of the signed value the wire protocol actually expects.
        Assert.True(RustPlusTokenFormat.TryNormalize(4292345678, out var token));
        Assert.Equal(-2621618, token);
    }

    [Fact]
    public void PositiveSignedToken_PassesThrough()
    {
        Assert.True(RustPlusTokenFormat.TryNormalize(123456789, out var token));
        Assert.Equal(123456789, token);
    }

    [Fact]
    public void Zero_IsValid()
    {
        Assert.True(RustPlusTokenFormat.TryNormalize(0, out var token));
        Assert.Equal(0, token);
    }

    [Fact]
    public void IntMinValue_PassesThrough()
    {
        Assert.True(RustPlusTokenFormat.TryNormalize(int.MinValue, out var token));
        Assert.Equal(int.MinValue, token);
    }

    [Fact]
    public void UintMaxValue_NormalizesToNegativeOne()
    {
        Assert.True(RustPlusTokenFormat.TryNormalize(uint.MaxValue, out var token));
        Assert.Equal(-1, token);
    }

    [Fact]
    public void ValueAboveUintRange_Rejected()
    {
        Assert.False(RustPlusTokenFormat.TryNormalize((long)uint.MaxValue + 1, out var token));
        Assert.Equal(0, token);
    }

    [Fact]
    public void NegativeBelowIntRange_Rejected()
    {
        Assert.False(RustPlusTokenFormat.TryNormalize((long)int.MinValue - 1, out var token));
        Assert.Equal(0, token);
    }
}
