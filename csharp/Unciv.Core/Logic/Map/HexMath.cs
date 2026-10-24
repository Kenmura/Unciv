using System.Globalization;
using Unciv.Mathematics;

namespace Unciv.Logic.Map;

public static class HexMath
{
    private static int RoundToInt(float value) => (int)Math.Floor(value + 0.5f);

    private static int Mod(int value, int m)
    {
        int r = value % m;
        return r < 0 ? r + m : r;
    }

    public static Vector2 GetVectorForAngle(float angle)
    {
        return new Vector2((float)Math.Sin(angle), (float)Math.Cos(angle));
    }

    private static Vector2 GetVectorByClockHour(int hour)
    {
        return GetVectorForAngle((float)(2 * Math.PI * (hour / 12f)));
    }

    /// <summary>
    /// Radius around origin tile -> the number of tiles in a hexagonal map including origin tile.
    /// </summary>
    public static int GetNumberOfTilesInHexagon(int size)
    {
        if (size < 0) return 0;
        return 1 + 6 * size * (size + 1) / 2;
    }

    /// <summary>Almost inverse of <see cref="GetNumberOfTilesInHexagon"/> - get equivalent fractional Hexagon radius for an Area.</summary>
    public static float GetHexagonalRadiusForArea(int numberOfTiles) =>
        numberOfTiles < 1 ? 0f : ((float)Math.Sqrt(12f * numberOfTiles - 3) - 3f) / 6f;

    public static int GetLatitude(HexCoord hexCoord) => hexCoord.x + hexCoord.y;

    public static int GetLongitude(HexCoord hexCoord) => hexCoord.x - hexCoord.y;

    /// <summary>
    /// Convert a latitude and longitude back into a hex coordinate. Inverse of <see cref="GetLatitude"/> and <see cref="GetLongitude"/>.
    /// </summary>
    public static Vector2 HexFromLatLong(int latitude, int longitude)
    {
        float y = (latitude - longitude) / 2f;
        float x = longitude + y;
        return new Vector2(x, y);
    }

    /// <summary>Convert hex latitude and longitude into world coordinates.</summary>
    public static Vector2 WorldFromLatLong(Vector2 vector, float tileRadius)
    {
        float x = vector.x * tileRadius * 1.5f * -1f;
        float y = vector.y * tileRadius * (float)Math.Sqrt(3f) * 0.5f;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Returns a vector containing width and height a rectangular map should have to have
    /// approximately the same number of tiles as an hexagonal map given a height/width ratio.
    /// </summary>
    public static HexCoord GetEquivalentRectangularSize(int size, float ratio = 0.65f)
    {
        if (size < 0)
            return HexCoord.Of(0, 0);

        int nTiles = GetNumberOfTilesInHexagon(size);
        int width = RoundToInt((float)Math.Sqrt(nTiles / ratio));
        int height = RoundToInt(width * ratio);
        return HexCoord.Of(width, height);
    }

    /// <summary>
    /// Returns a radius of a hexagonal map that has approximately the same number of
    /// tiles as a rectangular map of a given width/height.
    /// </summary>
    public static int GetEquivalentHexagonalRadius(int width, int height) =>
        RoundToInt(GetHexagonalRadiusForArea(width * height));

    public static Vector2 GetUnwrappedNearestTo(HexCoord unwrapHexCoord, HexCoord staticHexCoord, int longitudinalRadius)
    {
        int referenceLong = GetLongitude(staticHexCoord);
        int toWrapLat = GetLatitude(unwrapHexCoord);
        int toWrapLong = GetLongitude(unwrapHexCoord);
        int wrappedLong = Mod(toWrapLong - referenceLong + longitudinalRadius, longitudinalRadius * 2)
                          - longitudinalRadius + referenceLong;
        return HexFromLatLong(toWrapLat, wrappedLong);
    }

    public static Vector2 Hex2WorldCoords(HexCoord hexCoord)
    {
        var xVector = GetVectorByClockHour(10);
        xVector.Scl((float)Math.Sqrt(3.0) * hexCoord.x);

        var yVector = GetVectorByClockHour(2);
        yVector.Scl((float)Math.Sqrt(3.0) * hexCoord.y);

        return xVector.Add(yVector);
    }

    public static Vector2 World2HexCoords(Vector2 worldCoord)
    {
        var D = GetVectorByClockHour(10);
        D.Scl((float)Math.Sqrt(3.0));
        var A = GetVectorByClockHour(2);
        A.Scl((float)Math.Sqrt(3.0));
        float den = D.x * A.y - D.y * A.x;
        float x = (worldCoord.x * A.y - worldCoord.y * A.x) / den;
        float y = (worldCoord.y * D.x - worldCoord.x * D.y) / den;
        return new Vector2(x, y);
    }

    public static int GetRow(HexCoord hexCoord) => (hexCoord.x + hexCoord.y) / 2;

    public static int GetColumn(HexCoord hexCoord) => hexCoord.y - hexCoord.x;

    public static HexCoord GetTileCoordsFromColumnRow(int column, int row)
    {
        int twoRows = row * 2;
        if (Math.Abs(column) % 2 == 1) twoRows += 1;

        return HexCoord.Of((twoRows - column) / 2, (twoRows + column) / 2);
    }

    public static Vector2 RoundHexCoords(Vector2 hexCoord)
    {
        static Vector3 RoundCubicCoords(Vector3 cubicCoords)
        {
            float rx = MathF.Round(cubicCoords.x, MidpointRounding.ToEven);
            float ry = MathF.Round(cubicCoords.y, MidpointRounding.ToEven);
            float rz = MathF.Round(cubicCoords.z, MidpointRounding.ToEven);

            float deltaX = Math.Abs(rx - cubicCoords.x);
            float deltaY = Math.Abs(ry - cubicCoords.y);
            float deltaZ = Math.Abs(rz - cubicCoords.z);

            if (deltaX > deltaY && deltaX > deltaZ)
                rx = -ry - rz;
            else if (deltaY > deltaZ)
                ry = -rx - rz;
            else
                rz = -rx - ry;

            return new Vector3(rx, ry, rz);
        }

        static Vector3 Hex2CubicCoords(Vector2 coord) =>
            new(coord.y - coord.x, coord.x, -coord.y);

        static Vector2 Cubic2HexCoords(Vector3 coord) =>
            new(coord.y, -coord.z);

        return Cubic2HexCoords(RoundCubicCoords(Hex2CubicCoords(hexCoord)));
    }

    public static List<HexCoord> GetHexCoordsAtDistance(HexCoord origin, int distance, int maxDistance, bool worldWrap)
    {
        var vectors = new List<HexCoord>();
        if (distance == 0)
        {
            return new List<HexCoord> { origin.Cpy() };
        }

        HexCoord GetHexcoordOnOtherSideOfClock(HexCoord vector) =>
            origin.Times(2).Minus(vector);

        var current = origin.Minus(HexCoord.Of(distance, distance)); // start at 6 o'clock
        for (int i = 0; i < distance; i++) // From 6 to 8
        {
            vectors.Add(current);
            vectors.Add(GetHexcoordOnOtherSideOfClock(current));
            current = current.Plus(1, 0);
        }
        for (int i = 0; i < distance; i++) // 8 to 10
        {
            vectors.Add(current.Cpy());
            if (!worldWrap || distance != maxDistance)
                vectors.Add(GetHexcoordOnOtherSideOfClock(current));
            current = current.Plus(1, 1);
        }
        for (int i = 0; i < distance; i++) // 10 to 12
        {
            vectors.Add(current.Cpy());
            if (!worldWrap || distance != maxDistance || i != 0)
                vectors.Add(GetHexcoordOnOtherSideOfClock(current));
            current = current.Plus(0, 1);
        }
        return vectors;
    }

    public static List<HexCoord> GetHexCoordsInDistance(HexCoord origin, int distance, bool worldWrap)
    {
        var hexesToReturn = new List<HexCoord>();
        for (int i = 0; i <= distance; i++)
        {
            hexesToReturn.AddRange(GetHexCoordsAtDistance(origin, i, distance, worldWrap));
        }
        return hexesToReturn;
    }

    public static int GetDistance(Vector2 origin, Vector2 destination) =>
        GetDistance((int)origin.x, (int)origin.y, (int)destination.x, (int)destination.y);

    public static int GetDistance(HexCoord origin, HexCoord destination) =>
        GetDistance(origin.x, origin.y, destination.x, destination.y);

    public static int GetDistance(int originX, int originY, int destinationX, int destinationY)
    {
        int relativeX = originX - destinationX;
        int relativeY = originY - destinationY;
        return (relativeX * relativeY >= 0)
            ? Math.Max(Math.Abs(relativeX), Math.Abs(relativeY))
            : (Math.Abs(relativeX) + Math.Abs(relativeY));
    }

    private static readonly Dictionary<int, HexCoord> ClockPositionToHexcoordMap = new()
    {
        [0] = HexCoord.Of(1, 1),
        [12] = HexCoord.Of(1, 1),
        [2] = HexCoord.Of(0, 1),
        [4] = HexCoord.Of(-1, 0),
        [6] = HexCoord.Of(-1, -1),
        [8] = HexCoord.Of(0, -1),
        [10] = HexCoord.Of(1, 0)
    };

    public static HexCoord GetClockPositionToHexcoord(int clockPosition) =>
        ClockPositionToHexcoordMap.TryGetValue(clockPosition, out var coord) ? coord : HexCoord.Zero;

    private static readonly Dictionary<int, Vector2> ClockPositionToWorldVectorMap = new()
    {
        [2] = Hex2WorldCoords(HexCoord.Of(0, -1)),
        [4] = Hex2WorldCoords(HexCoord.Of(1, 0)),
        [6] = Hex2WorldCoords(HexCoord.Of(1, 1)),
        [8] = Hex2WorldCoords(HexCoord.Of(0, 1)),
        [10] = Hex2WorldCoords(HexCoord.Of(-1, 0)),
        [12] = Hex2WorldCoords(HexCoord.Of(-1, -1))
    };

    public static Vector2 GetClockPositionToWorldVector(int clockPosition) =>
        ClockPositionToWorldVectorMap.TryGetValue(clockPosition, out var vec) ? vec : Vector2.Zero;

    public static int GetZeroBasedIndex(int x, int y)
    {
        if (x == 0 && y == 0) return 0;
        int ring = GetDistance(0, 0, x, y);
        int ringStart = 1 + 6 * ring * (ring - 1) / 2;

        int positionInRing;
        if (ring == y) positionInRing = 0 + x;
        else if (ring == x) positionInRing = ring + 1 + y;
        else if (ring == -x) positionInRing = 2 * ring + 1 - y;
        else if (ring == -y) positionInRing = 3 * ring + 2 - x;
        else if (ring == x - y) positionInRing = 4 * ring + 2 + x - 1;
        else if (ring == y - x) positionInRing = 5 * ring + 1 + y - 1;
        else throw new InvalidOperationException("How???");

        return ringStart + positionInRing;
    }

    public static int MapRelativePositionToPositiveIntRedblob(int x, int y)
    {
        if (x == 0 && y == 0) return 0;
        int ring = GetDistance(0, 0, x, y);
        int ringStart = 1 + 6 * ring * (ring - 1) / 2;
        var vectorsInRing = GetHexCoordsAtDistance(HexCoord.Zero, ring, ring, false);
        int positionInRing = vectorsInRing.IndexOf(HexCoord.Of(x, y));
        return ringStart + positionInRing;
    }
}

public readonly record struct HexCoord(int x = 0, int y = 0)
{
    public int X => x;
    public int Y => y;

    public static readonly HexCoord Zero = new(0, 0);

    public static HexCoord Of(int x, int y) => new(x, y);
    public static HexCoord of(int x, int y) => new(x, y);

    public HexCoord Plus(HexCoord hexCoord) => new(x + hexCoord.x, y + hexCoord.y);
    public HexCoord plus(HexCoord hexCoord) => Plus(hexCoord);

    public HexCoord Plus(int plusX, int plusY) => new(x + plusX, y + plusY);
    public HexCoord plus(int plusX, int plusY) => Plus(plusX, plusY);

    public HexCoord Minus(HexCoord hexCoord) => new(x - hexCoord.x, y - hexCoord.y);
    public HexCoord minus(HexCoord hexCoord) => Minus(hexCoord);

    public HexCoord Times(int factor) => new(x * factor, y * factor);
    public HexCoord times(int factor) => Times(factor);

    public Vector2 ToVector2() => new(x, y);
    public Vector2 toVector2() => ToVector2();

    public bool Eq(int x, int y) => this.x == x && this.y == y;
    public bool eq(int x, int y) => Eq(x, y);

    public HexCoord Cpy() => this;
    public HexCoord cpy() => this;

    public HexCoord ToHexCoord() => this;
    public HexCoord toHexCoord() => this;

    public HexCoord AsSerializable() => new(x, y);
    public HexCoord asSerializable() => AsSerializable();

    public string ToPrettyString() => $"({x},{y})";
    public string toPrettyString() => ToPrettyString();

    public override string ToString() => $"HexCoord(x={x}, y={y})";

    public static HexCoord FromString(string str)
    {
        int commaPos = str.IndexOf(',');
        if (commaPos < 1 || !str.StartsWith('(') || !str.EndsWith(')'))
            throw new FormatException($"\"{str}\" is not a valid HexCoord representation");
        int x = int.Parse(str.AsSpan(1, commaPos - 1), CultureInfo.InvariantCulture);
        int y = int.Parse(str.AsSpan(commaPos + 1, str.Length - commaPos - 2), CultureInfo.InvariantCulture);
        return new HexCoord(x, y);
    }
    public static HexCoord fromString(string str) => FromString(str);

    public static HexCoord operator +(HexCoord a, HexCoord b) => a.Plus(b);
    public static HexCoord operator -(HexCoord a, HexCoord b) => a.Minus(b);
    public static HexCoord operator *(HexCoord a, int factor) => a.Times(factor);
}

public readonly record struct InlineHexCoord(int coords = 0)
{
    public int x => coords >> 16;
    public int y => (coords << 16) >> 16;
    public int X => x;
    public int Y => y;

    public override string ToString() => $"HexCoord(x={x}, y={y})";

    public static InlineHexCoord Of(int x, int y)
    {
        if (x < -32768 || x > 32767) throw new ArgumentOutOfRangeException(nameof(x), "X value must be in range -32768..32767");
        if (y < -32768 || y > 32767) throw new ArgumentOutOfRangeException(nameof(y), "Y value must be in range -32768..32767");
        return new InlineHexCoord((x << 16) | (y & 0xFFFF));
    }
}

public static class Vector2HexExtensions
{
    public static HexCoord ToHexCoord(this Vector2 vector) => HexCoord.Of((int)vector.x, (int)vector.y);
}
