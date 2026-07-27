using Rustex.Domain.RustPlus;
using Xunit;

namespace Rustex.Api.Tests.RustPlus;

/// <summary>
/// Expected values were computed independently in a standalone JS port of the same source
/// algorithm (rustplusplus's map.js), not derived from GridConverter.cs itself — so these tests
/// catch a transcription error in the C# port rather than just re-asserting whatever the port
/// happens to compute.
/// </summary>
public class GridConverterTests
{
    [Theory]
    [InlineData(1, "A")]
    [InlineData(25, "Y")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(51, "AY")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    [InlineData(702, "ZZ")]
    [InlineData(703, "AAA")]
    public void NumberToLetters_BijectiveBase26_MatchesIndependentComputation(int num, string expected)
    {
        Assert.Equal(expected, GridConverter.NumberToLetters(num));
    }

    [Theory]
    [InlineData(3000, 2925f)]
    [InlineData(3500, 3510f)]
    [InlineData(4000, 3948.75f)]
    [InlineData(4250, 4241.25f)]
    [InlineData(4500, 4387.5f)]
    public void GetCorrectedMapSize_SnapsToGridBoundary(int mapSize, float expectedCorrected)
    {
        Assert.Equal(expectedCorrected, GridConverter.GetCorrectedMapSize(mapSize), precision: 2);
    }

    [Theory]
    [InlineData(3000, "A19")]
    [InlineData(3500, "A23")]
    [InlineData(4000, "A26")]
    [InlineData(4250, "A28")]
    [InlineData(4500, "A29")]
    public void ToGrid_Origin_MatchesIndependentComputation(int mapSize, string expected)
    {
        Assert.Equal(expected, GridConverter.ToGrid(0, 0, mapSize));
    }

    [Theory]
    [InlineData(3000, "K9")]
    [InlineData(3500, "L12")]
    [InlineData(4000, "N13")]
    [InlineData(4250, "O14")]
    [InlineData(4500, "P14")]
    public void ToGrid_Center_MatchesIndependentComputation(int mapSize, string expected)
    {
        Assert.Equal(expected, GridConverter.ToGrid(mapSize / 2f, mapSize / 2f, mapSize));
    }

    [Theory]
    [InlineData(3000)]
    [InlineData(3500)]
    [InlineData(4000)]
    [InlineData(4250)]
    [InlineData(4500)]
    public void ToGrid_WellOutsideMapBounds_ReturnsNull(int mapSize)
    {
        Assert.Null(GridConverter.ToGrid(-50, 100, mapSize));
        Assert.Null(GridConverter.ToGrid(100, mapSize + 500, mapSize));
    }

    [Fact]
    public void ToGrid_NegativeCoordinates_DoNotThrow()
    {
        var result = GridConverter.ToGrid(-4000, -4000, 4000);
        Assert.Null(result);
    }
}
