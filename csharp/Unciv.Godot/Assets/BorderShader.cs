using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// T377: the territory border shader (design/BORDERS.md). The vertex stage lays the band out in screen pixels: every vertex is a hex corner on the tile's true side, <c>UV</c>/<c>UV2</c> are the ground normals
/// (pointing into the owned tile) of the side arriving at and leaving the corner, <c>COLOR</c> = (across, opacity, end fade). The two normals are projected with the live camera, turned into the screen normals
/// of the two sides and averaged to a miter, so a band keeps <c>band_px</c> pixels at every tilt, yaw and zoom and joins at every corner. The fragment stage paints the casing (outer share of the band) and the
/// body, anti-aliased by <c>fwidth</c>. Two materials share the source: the solid one is depth tested, the ghost one is drawn only where something is in front of the border (inverted depth test) at a lower opacity.
/// Written for the spatial shader language every renderer supports.
/// </summary>
public static class BorderShader
{
    /// <summary>Largest factor of the miter (1 / cos of half the screen turning angle); past it the offset is limited, a bevel that keeps the strip connected.</summary>
    public const float MiterLimit = 3f;

    /// <summary>Opacity of the ghost pass relative to the solid one.</summary>
    public const float GhostOpacity = 0.35f;

    /// <summary>World units the solid pass is pulled towards the camera, only against z-fighting with the terrain.</summary>
    public const float DepthBias = 0.1f;

    private const string Body = """
        uniform vec4 outer_colour : source_color = vec4(1.0);
        uniform vec4 casing_colour : source_color = vec4(0.0, 0.0, 0.0, 1.0);
        uniform float band_px = 4.0;
        uniform float casing_share = 0.3;
        uniform float opacity_scale = 1.0;
        uniform float depth_bias = 0.1;
        uniform float miter_limit = 3.0;
        varying float v_across;
        varying float v_alpha;

        vec4 clip_of(vec3 p, mat4 model_view, mat4 proj) {
            vec4 v = model_view * vec4(p, 1.0);
            v.z += depth_bias;
            return proj * v;
        }

        vec2 px_of(vec4 c, vec2 viewport) {
            return c.xy / c.w * viewport * 0.5;
        }

        // The screen normal of the side whose inward ground normal is n, at ground point p (pixel space, pointing inwards)
        vec2 screen_normal(vec3 p, vec2 n, vec2 origin, mat4 mv, mat4 proj, vec2 vp) {
            vec3 travel = vec3(-n.y, 0.0, n.x);
            vec2 t = px_of(clip_of(p + travel, mv, proj), vp) - origin;
            vec2 inward = px_of(clip_of(p + vec3(n.x, 0.0, n.y), mv, proj), vp) - origin;
            vec2 perp = vec2(-t.y, t.x);
            if (dot(perp, inward) < 0.0) perp = -perp;
            float len = length(perp);
            return len > 1e-6 ? perp / len : normalize(inward + vec2(1e-6));
        }

        void vertex() {
            vec3 p = VERTEX;
            mat4 mv = MODELVIEW_MATRIX;
            mat4 proj = PROJECTION_MATRIX;
            vec2 vp = VIEWPORT_SIZE;
            vec4 clip = clip_of(p, mv, proj);
            vec2 origin = px_of(clip, vp);
            vec2 n1 = screen_normal(p, UV, origin, mv, proj, vp);
            vec2 n2 = screen_normal(p, UV2, origin, mv, proj, vp);
            vec2 m = n1 + n2;
            m = length(m) > 1e-4 ? normalize(m) : n1;
            float k = min(1.0 / max(dot(m, n1), 1e-3), miter_limit);
            vec2 offset = m * k * band_px * COLOR.r;
            clip.xy += offset * 2.0 / vp * clip.w;
            POSITION = clip;
            v_across = COLOR.r;
            v_alpha = COLOR.g * COLOR.b;
        }

        void fragment() {
            float fw = max(fwidth(v_across), 1e-4);
            float body = smoothstep(casing_share - fw * 0.5, casing_share + fw * 0.5, v_across);
            ALBEDO = mix(casing_colour.rgb, outer_colour.rgb, body);
            float cover = clamp(v_across / fw + 0.5, 0.0, 1.0) * clamp((1.0 - v_across) / fw + 0.5, 0.0, 1.0);
            ALPHA = cover * v_alpha * opacity_scale;
        }
        """;

    /// <summary>The solid pass: depth tested, no depth write.</summary>
    public static readonly string SolidSource = "shader_type spatial;\nrender_mode unshaded, cull_disabled, depth_draw_never, blend_mix;\n" + Body;

    /// <summary>The ghost pass: drawn only where the border is hidden by something in front of it.</summary>
    public static readonly string GhostSource = "shader_type spatial;\nrender_mode unshaded, cull_disabled, depth_draw_never, depth_test_inverted, blend_mix;\n" + Body;

    /// <summary>The per-vertex colour attribute the shader reads (across, opacity, fade); not a colour for display.</summary>
    public static Color Attribute(BorderVertex v) => new(v.Across, v.Alpha, v.Fade, 1f);

    /// <summary>A material of the border: <paramref name="ghost"/> selects the hidden-part pass.</summary>
    public static ShaderMaterial Material(bool ghost, Color outer, Color casing)
    {
        var shader = new Shader { Code = ghost ? GhostSource : SolidSource };
        var material = new ShaderMaterial { Shader = shader, RenderPriority = ghost ? 0 : 1 };
        material.SetShaderParameter("outer_colour", outer);
        material.SetShaderParameter("casing_colour", casing);
        material.SetShaderParameter("opacity_scale", ghost ? GhostOpacity : 1f);
        material.SetShaderParameter("depth_bias", ghost ? 0f : DepthBias);
        material.SetShaderParameter("miter_limit", MiterLimit);
        return material;
    }
}
