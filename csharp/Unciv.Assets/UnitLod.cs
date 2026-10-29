namespace Unciv.Assets;

/// <summary>
/// C085: level of detail and draw caps of unit figures (design/UNIT-MODELS.md section 6). Pure, so the thresholds are tested without Godot.
/// A figure's idle pose is held (never advances with the frame clock), so there is no idle animation to pause: the cost of a unit is its
/// figures' meshes and skeletons, and these rules cut it. Zoomed far out a formation draws one figure (the lead, at lone-figure size and in the
/// tile's middle) because the others are only a few pixels; a unit well outside the camera's ground footprint is not drawn at all.
/// </summary>
public static class UnitLod
{
    /// <summary>Camera size (<c>MapView.Zoom</c>, larger = further out) from which formations draw a single figure. The default view is 14, the farthest 40.</summary>
    public const float FarZoom = 22f;

    /// <summary>How far outside the camera's ground footprint (world units) a unit still draws: the footprint is the view on the ground plane, and at the lowest tilt (25 degrees) a point 1.9 above it (status marks over a hill) shows 4 further out, so a unit at the screen edge, its marks and a lunge never pop.</summary>
    public const float ViewMargin = 5f;

    /// <summary>Whether a camera of size <paramref name="zoom"/> is far enough out for the reduced level; NaN counts as near.</summary>
    public static bool IsFar(float zoom) => zoom >= FarZoom;

    /// <summary>Figures a formation of <paramref name="count"/> draws: all of them near, the lead only when far (at least one for any real unit, none for a count of zero or less).</summary>
    public static int FiguresDrawn(int count, bool far) => count <= 0 ? 0 : far ? 1 : count;

    /// <summary>Whether a unit standing at ground (<paramref name="x"/>, <paramref name="z"/>) is drawn when the camera's footprint is <paramref name="view"/>; no footprint (the camera is not set up or looks at the sky) draws everything.</summary>
    public static bool IsDrawn(float x, float z, GroundRect? view)
    {
        if (view is not { } rect) return true;
        var padded = rect.Expand(ViewMargin);
        return x >= padded.MinX && x <= padded.MaxX && z >= padded.MinZ && z <= padded.MaxZ;
    }
}
