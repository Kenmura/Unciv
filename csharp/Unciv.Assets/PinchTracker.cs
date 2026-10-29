using System.Numerics;

namespace Unciv.Assets;

/// <summary>
/// C096: the two-finger gesture of the map as pure arithmetic. Fingers are tracked by index; with two down, every move yields the zoom factor (new distance
/// over the previous one, above 1 when the fingers spread) and the movement of their midpoint (the pan). Fewer or more than two fingers yield nothing.
/// </summary>
public sealed class PinchTracker
{
    private readonly Dictionary<int, Vector2> fingers = [];
    private bool active;
    private Vector2 lastMid;
    private float lastDistance;

    /// <summary>Fingers currently down.</summary>
    public int Count => fingers.Count;

    /// <summary>True while exactly two fingers are down (a pinch is in progress).</summary>
    public bool Active => active;

    /// <summary>A finger landed. True when this made two fingers: the pinch starts (the caller cancels any one-finger press).</summary>
    public bool Down(int index, Vector2 position)
    {
        fingers[index] = position;
        if (fingers.Count != 2) { active = false; return false; }
        Restart();
        return true;
    }

    /// <summary>Forgets every finger (focus lost, view left): a release that never arrived must not leave a ghost finger that turns the next touch into a pinch.</summary>
    public void Reset()
    {
        fingers.Clear();
        active = false;
    }

    /// <summary>A finger lifted; the pinch ends (a finger that remains does not pan or tap until it lifts and lands again).</summary>
    public void Up(int index)
    {
        fingers.Remove(index);
        active = false;
    }

    /// <summary>A finger moved. Null unless a pinch is active and the finger is one of its two.</summary>
    public (float ZoomFactor, Vector2 MidpointDelta, Vector2 Midpoint)? Move(int index, Vector2 position)
    {
        if (!fingers.ContainsKey(index)) return null;
        fingers[index] = position;
        if (!active) return null;
        var (mid, distance) = Measure();
        float factor = lastDistance > 0.001f && distance > 0.001f ? distance / lastDistance : 1f;
        var result = (factor, mid - lastMid, mid);
        lastMid = mid;
        lastDistance = distance;
        return result;
    }

    private void Restart()
    {
        active = true;
        (lastMid, lastDistance) = Measure();
    }

    private (Vector2 Mid, float Distance) Measure()
    {
        var points = fingers.Values.ToArray();
        return ((points[0] + points[1]) / 2f, Vector2.Distance(points[0], points[1]));
    }
}
