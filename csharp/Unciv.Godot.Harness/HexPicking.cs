using Godot;

namespace Unciv.GodotHarness;

public static class HexPicking
{
    /// <summary>The point on the horizontal plane <c>y = <paramref name="planeY"/></c> under a screen position, or null if the ray misses it.</summary>
    public static Vector3? PickGroundPoint(Camera3D camera, Vector2 screenPosition, float planeY = 0f)
    {
        var origin = camera.ProjectRayOrigin(screenPosition);
        var direction = camera.ProjectRayNormal(screenPosition);
        return new Plane(Vector3.Up, planeY).IntersectsRay(origin, direction);
    }
}
