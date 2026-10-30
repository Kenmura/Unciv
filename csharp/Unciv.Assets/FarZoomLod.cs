namespace Unciv.Assets;

/// <summary>
/// T287: far-zoom draw-call reduction. On a Large map zoomed all the way out the client issued 13.3k draw calls (CPU bound on every GPU): about 6k for the yield icons (a sprite and a
/// label each), 6k for the feature and resource prop models (one node per tile, none batchable) and the rest for cities and units. Beyond these camera sizes the yield icons are hidden
/// (a handful of pixels, amounts unreadable) and the prop models are replaced by one merged mesh per terrain chunk with identical geometry and colours. Pure, so the thresholds are tested
/// without Godot. The default view is 14, the farthest 40; the harness goldens are at 14 and below, so they never see either switch.
/// </summary>
public static class FarZoomLod
{
    /// <summary>Camera size (<c>MapView.Zoom</c>, larger = further out) from which the yield icons are hidden: about 45 px per world unit at 1080p, an icon under 11 px.</summary>
    public const float HideYieldsZoom = 24f;

    /// <summary>Camera size from which the per-tile prop models give way to the merged per-chunk meshes (the same look, far fewer draw calls); matches the unit formations' far level.</summary>
    public const float MergePropsZoom = UnitLod.FarZoom;

    /// <summary>Whether a camera of size <paramref name="zoom"/> is too far out for yield icons; NaN counts as near.</summary>
    public static bool HideYields(float zoom) => zoom >= HideYieldsZoom;

    /// <summary>Whether a camera of size <paramref name="zoom"/> draws the merged prop meshes instead of the prop models; NaN counts as near.</summary>
    public static bool MergeProps(float zoom) => zoom >= MergePropsZoom;
}
