using Godot;
using Unciv.Assets;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T377: measures the rendered border on screen. Two captures of the same view, one with the border strips and one without; a probe point is "painted" when the two differ there. Probe points follow the
/// shader's own layout (the band lies inward of the true side, perpendicular to it on screen): along every side of the given chains and at every corner, where a notch or a gap would show.
/// </summary>
internal static class BorderProbe
{
    /// <summary>Height of the border centre line on flat land (rim 0.2 plus the lift).</summary>
    private const float Height = 0.23f;
    private const int MinDifference = 30;

    internal sealed record Result(int Samples, int Painted, int Corners, int CornersPainted, List<string> Misses);

    public static Result Run(Camera3D camera, RgbaImage with, RgbaImage without, IReadOnlyList<BorderChain> chains, Func<Unciv.Assets.BorderEdge, bool> include, int bandPixels)
    {
        var samples = 0;
        var painted = 0;
        var corners = 0;
        var cornersPainted = 0;
        var misses = new List<string>();
        void Probe(Vector2 at, string what, bool corner)
        {
            if (at.X < 1 || at.Y < 1 || at.X >= with.Width - 1 || at.Y >= with.Height - 1) return;
            samples++;
            if (corner) corners++;
            var diff = Diff(with, without, (int)at.X, (int)at.Y);
            if (diff >= MinDifference)
            {
                painted++;
                if (corner) cornersPainted++;
            }
            else if (misses.Count < 12) misses.Add($"{what} at {at}");
        }
        foreach (var chain in chains)
        {
            var count = chain.Sides.Count;
            for (var i = 0; i < count; i++)
            {
                if (!include(chain.Sides[i])) continue;
                var from = chain.Points[i];
                var to = chain.Points[chain.Closed ? (i + 1) % count : i + 1];
                var n = Normal(from, to);
                var sideNormal = ScreenNormal(camera, from, n);
                foreach (var t in new[] { 0.25f, 0.5f, 0.75f })
                {
                    var p = new BorderPoint(from.X + (to.X - from.X) * t, from.Z + (to.Z - from.Z) * t);
                    Probe(Screen(camera, p) + sideNormal * (0.65f * bandPixels), $"side {chain.Sides[i]} t={t}", false);
                }
                // The corner at the start of this side joins it to the previous one
                var previous = chain.Closed ? (i + count - 1) % count : i - 1;
                if (previous < 0 || !include(chain.Sides[previous])) continue;
                var before = Normal(chain.Points[chain.Closed ? previous : i - 1], from);
                var nBefore = ScreenNormal(camera, from, before);
                var miter = nBefore + sideNormal;
                if (miter.Length() < 1e-3f) continue;
                miter = miter.Normalized();
                var k = 1f / Math.Max(miter.Dot(sideNormal), 1e-3f);
                if (k >= 3f) continue; // past the miter limit the shader bevels
                Probe(Screen(camera, from) + miter * (0.65f * bandPixels * k), $"corner {chain.Sides[i]}", true);
            }
        }
        return new Result(samples, painted, corners, cornersPainted, misses);
    }

    /// <summary>Number of pixels, along the screen normal through the middle of a side, that differ between the two captures: the band's width on screen.</summary>
    public static float WidthAcross(Camera3D camera, RgbaImage with, RgbaImage without, BorderPoint from, BorderPoint to)
    {
        var mid = new BorderPoint((from.X + to.X) / 2f, (from.Z + to.Z) / 2f);
        var normal = ScreenNormal(camera, mid, Normal(from, to));
        var start = Screen(camera, mid) - normal * 8f;
        var covered = 0f;
        for (var step = 0; step < 64; step++) // 0.25 px steps over 16 px
        {
            var at = start + normal * (step * 0.25f);
            if (at.X < 1 || at.Y < 1 || at.X >= with.Width - 1 || at.Y >= with.Height - 1) continue;
            if (Diff(with, without, (int)at.X, (int)at.Y) >= MinDifference) covered += 0.25f;
        }
        return covered;
    }

    private static int Diff(RgbaImage a, RgbaImage b, int x, int y)
    {
        var i = (y * a.Width + x) * 4;
        return Math.Abs(a.Pixels[i] - b.Pixels[i]) + Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]) + Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
    }

    private static Vector2 Screen(Camera3D camera, BorderPoint p) => camera.UnprojectPosition(new Vector3(p.X, Height, p.Z));

    /// <summary>Unit ground normal pointing into the owned tile (the engine's travel direction has the owned tile on its left, world z is minus y).</summary>
    private static Vector2 Normal(BorderPoint from, BorderPoint to)
    {
        var a = to.X - from.X;
        var b = to.Z - from.Z;
        var length = MathF.Sqrt(a * a + b * b);
        return new Vector2(b / length, -a / length);
    }

    /// <summary>The same construction as the vertex shader: the screen perpendicular of the projected side, turned to agree with the projected inward normal.</summary>
    private static Vector2 ScreenNormal(Camera3D camera, BorderPoint at, Vector2 n)
    {
        var origin = Screen(camera, at);
        var travel = Screen(camera, new BorderPoint(at.X - n.Y * 0.1f, at.Z + n.X * 0.1f)) - origin;
        var inward = Screen(camera, new BorderPoint(at.X + n.X * 0.1f, at.Z + n.Y * 0.1f)) - origin;
        var perp = new Vector2(-travel.Y, travel.X);
        if (perp.Dot(inward) < 0f) perp = -perp;
        return perp.Normalized();
    }
}
