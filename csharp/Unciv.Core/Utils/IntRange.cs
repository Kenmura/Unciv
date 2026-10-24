namespace Unciv.Utils;

// kotlin.ranges.IntRange (closed, step 1): `first..last`. Empty when First > Last.
public readonly record struct IntRange(int First, int Last)
{
    public bool IsEmpty => First > Last;

    public override string ToString() => $"{First}..{Last}";
}
