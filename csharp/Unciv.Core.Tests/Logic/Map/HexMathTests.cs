using Unciv.Logic.Map;
using Unciv.Mathematics;

namespace Unciv.Core.Tests.Logic.Map;

public class HexMathTests
{
    // Ported from tests/src/com/unciv/logic/map/HexmathTests.kt
    [Fact]
    public void ZeroIndexed()
    {
        Assert.Equal(0, HexMath.GetZeroBasedIndex(0, 0));
    }

    [Fact]
    public void TestMappingIsOneToOne()
    {
        var seenCoordsMapping = new HashSet<int>();
        for (int ring = 1; ring <= 100; ring++)
        {
            var coords = HexMath.GetHexCoordsAtDistance(HexCoord.Zero, ring, 100, false);
            int ringStartCoordinate = 1 + 6 * ring * (ring - 1) / 2;
            foreach (var coord in coords)
            {
                int mapping = HexMath.GetZeroBasedIndex(coord.x, coord.y);
                Assert.DoesNotContain(mapping, seenCoordsMapping);
                Assert.InRange(mapping, ringStartCoordinate, ringStartCoordinate + 6 * ring - 1);
                seenCoordsMapping.Add(mapping);
            }
        }
    }

    [Fact]
    public void RedblobMappingIsOneToOne()
    {
        var seenCoordsMapping = new HashSet<int>();
        for (int ring = 1; ring <= 5; ring++)
        {
            var coords = HexMath.GetHexCoordsAtDistance(HexCoord.Zero, ring, 5, false);
            int ringStartCoordinate = 1 + 6 * ring * (ring - 1) / 2;
            foreach (var coord in coords)
            {
                int redblob = HexMath.MapRelativePositionToPositiveIntRedblob(coord.x, coord.y);
                Assert.DoesNotContain(redblob, seenCoordsMapping);
                Assert.InRange(redblob, ringStartCoordinate, ringStartCoordinate + 6 * ring - 1);
                seenCoordsMapping.Add(redblob);
            }
        }
    }

    [Fact]
    public void TestHexCoordConversions()
    {
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                var hexCoord = HexCoord.Of(x, y);
                Assert.Equal(x, hexCoord.x);
                Assert.Equal(y, hexCoord.y);
                Assert.Equal(x, hexCoord.X);
                Assert.Equal(y, hexCoord.Y);
                Assert.True(hexCoord.Eq(x, y));
            }
        }
    }

    [Fact]
    public void TestHexCoordFormatAndParseBack()
    {
        var rnd = new Random(42);
        int RndCoord() => rnd.Next(-20, 21);

        for (int i = 0; i < 42; i++)
        {
            var expected = HexCoord.Of(RndCoord(), RndCoord());
            string prettyString = expected.ToPrettyString();
            var actual = HexCoord.FromString(prettyString);
            Assert.Equal(expected, actual);
        }

        Assert.Throws<FormatException>(() => HexCoord.FromString("invalid"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("(1)"));
    }

    [Fact]
    public void HexCoordOperations()
    {
        var a = HexCoord.Of(1, 2);
        var b = HexCoord.Of(3, -1);
        Assert.Equal(HexCoord.Of(4, 1), a.Plus(b));
        Assert.Equal(HexCoord.Of(4, 1), a + b);
        Assert.Equal(HexCoord.Of(-2, 3), a.Minus(b));
        Assert.Equal(HexCoord.Of(-2, 3), a - b);
        Assert.Equal(HexCoord.Of(3, 6), a.Times(3));
        Assert.Equal(HexCoord.Of(3, 6), a * 3);
        Assert.Equal(HexCoord.Of(2, 4), a.Plus(1, 2));

        var v = a.ToVector2();
        Assert.Equal(1f, v.x);
        Assert.Equal(2f, v.y);
        Assert.Equal(a, v.ToHexCoord());
    }

    [Fact]
    public void InlineHexCoordPackingAndUnpacking()
    {
        for (int x = -50; x <= 50; x += 10)
        {
            for (int y = -50; y <= 50; y += 10)
            {
                var inlineCoord = InlineHexCoord.Of(x, y);
                Assert.Equal(x, inlineCoord.x);
                Assert.Equal(y, inlineCoord.y);
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => InlineHexCoord.Of(40000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InlineHexCoord.Of(0, -40000));
    }

    [Fact]
    public void NumberOfTilesInHexagonAndRadius()
    {
        Assert.Equal(0, HexMath.GetNumberOfTilesInHexagon(-1));
        Assert.Equal(1, HexMath.GetNumberOfTilesInHexagon(0));
        Assert.Equal(7, HexMath.GetNumberOfTilesInHexagon(1));
        Assert.Equal(19, HexMath.GetNumberOfTilesInHexagon(2));
        Assert.Equal(37, HexMath.GetNumberOfTilesInHexagon(3));

        Assert.Equal(0f, HexMath.GetHexagonalRadiusForArea(0));
        Assert.Equal(0f, HexMath.GetHexagonalRadiusForArea(1));
        Assert.True(System.Math.Abs(1f - HexMath.GetHexagonalRadiusForArea(7)) < 0.0001f);
        Assert.True(System.Math.Abs(2f - HexMath.GetHexagonalRadiusForArea(19)) < 0.0001f);
    }

    [Fact]
    public void LatitudeLongitudeRoundTrip()
    {
        var coord = HexCoord.Of(5, 3);
        int lat = HexMath.GetLatitude(coord);
        int lon = HexMath.GetLongitude(coord);
        Assert.Equal(8, lat);
        Assert.Equal(2, lon);

        var hexVec = HexMath.HexFromLatLong(lat, lon);
        Assert.Equal(5f, hexVec.x);
        Assert.Equal(3f, hexVec.y);
    }

    [Fact]
    public void ColumnRowConversions()
    {
        for (int x = -5; x <= 5; x++)
        {
            for (int y = -5; y <= 5; y++)
            {
                var coord = HexCoord.Of(x, y);
                int row = HexMath.GetRow(coord);
                int col = HexMath.GetColumn(coord);
                var recovered = HexMath.GetTileCoordsFromColumnRow(col, row);
                // Row conversion discards half-row when x+y is odd
                if ((x + y) % 2 == 0)
                {
                    Assert.Equal(coord, recovered);
                }
            }
        }
    }

    [Fact]
    public void DistanceCalculations()
    {
        Assert.Equal(0, HexMath.GetDistance(HexCoord.Zero, HexCoord.Zero));
        Assert.Equal(1, HexMath.GetDistance(HexCoord.Of(0, 0), HexCoord.Of(1, 0)));
        Assert.Equal(1, HexMath.GetDistance(HexCoord.Of(0, 0), HexCoord.Of(0, 1)));
        Assert.Equal(1, HexMath.GetDistance(HexCoord.Of(0, 0), HexCoord.Of(1, 1)));
        Assert.Equal(2, HexMath.GetDistance(HexCoord.Of(0, 0), HexCoord.Of(1, -1)));
        Assert.Equal(3, HexMath.GetDistance(HexCoord.Of(2, 2), HexCoord.Of(-1, 0)));
    }

    [Fact]
    public void WorldAndHexCoordConversions()
    {
        var origin = HexCoord.Of(0, 0);
        var worldOrigin = HexMath.Hex2WorldCoords(origin);
        Assert.True(worldOrigin.Len() < 0.0001f);

        for (int x = -3; x <= 3; x++)
        {
            for (int y = -3; y <= 3; y++)
            {
                var coord = HexCoord.Of(x, y);
                var world = HexMath.Hex2WorldCoords(coord);
                var recovered = HexMath.World2HexCoords(world);
                var rounded = HexMath.RoundHexCoords(recovered);
                Assert.Equal(coord.x, (int)System.Math.Round(rounded.x));
                Assert.Equal(coord.y, (int)System.Math.Round(rounded.y));
            }
        }
    }

    [Fact]
    public void HexCoordsInDistanceCount()
    {
        var ring0 = HexMath.GetHexCoordsInDistance(HexCoord.Zero, 0, false);
        Assert.Single(ring0);

        var ring1 = HexMath.GetHexCoordsInDistance(HexCoord.Zero, 1, false);
        Assert.Equal(7, ring1.Count);

        var ring2 = HexMath.GetHexCoordsInDistance(HexCoord.Zero, 2, false);
        Assert.Equal(19, ring2.Count);

        var ring3 = HexMath.GetHexCoordsInDistance(HexCoord.Zero, 3, false);
        Assert.Equal(37, ring3.Count);
    }

    [Fact]
    public void ClockPositionToHexcoordAndWorldVector()
    {
        Assert.Equal(HexCoord.Of(1, 1), HexMath.GetClockPositionToHexcoord(0));
        Assert.Equal(HexCoord.Of(1, 1), HexMath.GetClockPositionToHexcoord(12));
        Assert.Equal(HexCoord.Of(0, 1), HexMath.GetClockPositionToHexcoord(2));
        Assert.Equal(HexCoord.Of(-1, 0), HexMath.GetClockPositionToHexcoord(4));
        Assert.Equal(HexCoord.Of(-1, -1), HexMath.GetClockPositionToHexcoord(6));
        Assert.Equal(HexCoord.Of(0, -1), HexMath.GetClockPositionToHexcoord(8));
        Assert.Equal(HexCoord.Of(1, 0), HexMath.GetClockPositionToHexcoord(10));
        Assert.Equal(HexCoord.Zero, HexMath.GetClockPositionToHexcoord(5));

        Assert.True(HexMath.GetClockPositionToWorldVector(2).Len() > 0f);
        Assert.Equal(Vector2.Zero, HexMath.GetClockPositionToWorldVector(99));
    }

    [Fact]
    public void EquivalentRectangularSize()
    {
        Assert.Equal(HexCoord.Of(0, 0), HexMath.GetEquivalentRectangularSize(-1));
        var size = HexMath.GetEquivalentRectangularSize(10, 0.65f);
        Assert.True(size.x > 0);
        Assert.True(size.y > 0);
        int radius = HexMath.GetEquivalentHexagonalRadius(size.x, size.y);
        Assert.InRange(radius, 9, 11);
    }

    [Fact]
    public void UnwrappedNearestTo()
    {
        var unwrapped = HexMath.GetUnwrappedNearestTo(HexCoord.Of(10, 0), HexCoord.Of(0, 0), 20);
        Assert.Equal(10f, unwrapped.x);
        Assert.Equal(0f, unwrapped.y);
    }

    [Fact]
    public void FromStringFollowsKotlinToInt()
    {
        Assert.Equal(HexCoord.Of(-3, 4), HexCoord.FromString("(-3,4)"));
        Assert.Equal(HexCoord.Of(3, 4), HexCoord.FromString("(+3,+4)"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("( 3,4)"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("(3, 4)"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("(3,4 )"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("(99999999999,4)"));
        Assert.Throws<FormatException>(() => HexCoord.FromString("(3.5,4)"));
    }

    [Fact]
    public void Vector2ConversionsTruncateLikeKotlinToInt()
    {
        Assert.Equal(HexCoord.Of(1, -1), new Vector2(1.9f, -1.9f).ToHexCoord());
        Assert.Equal(HexCoord.Of(0, int.MaxValue), new Vector2(float.NaN, 3e9f).ToHexCoord());
        Assert.Equal(3, HexMath.GetDistance(new Vector2(0.9f, 0.9f), new Vector2(-3.9f, -2.9f)));
    }

    [Fact]
    public void EquivalentSizeUsesHalfUpRounding()
    {
        // 0.49999997f * 1 would round to 1 with a float add; Kotlin roundToInt gives 0 (covered directly in KotlinMathTests)
        Assert.Equal(0, HexMath.GetEquivalentHexagonalRadius(1, 1));
        Assert.Equal(HexCoord.Of(0, 0), HexMath.GetEquivalentRectangularSize(-1));
        // size 1: 7 tiles, width = round(sqrt(7/0.65f)) = round(3.2817) = 3, height = round(3*0.65f=1.95f) = 2
        Assert.Equal(HexCoord.Of(3, 2), HexMath.GetEquivalentRectangularSize(1));
    }

    [Fact]
    public void UnwrappedNearestToWrapsAroundTheSeam()
    {
        // longitude 15 relative to a reference at -15 with radius 20 is 30 away, so it wraps to longitude -25 (latitude stays 15)
        var unwrapped = HexMath.GetUnwrappedNearestTo(HexCoord.Of(15, 0), HexCoord.Of(-15, 0), 20);
        Assert.Equal(-5f, unwrapped.x);
        Assert.Equal(20f, unwrapped.y);
    }
}
