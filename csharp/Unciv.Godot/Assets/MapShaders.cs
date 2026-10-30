using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C098: the Ultra-only shader for the merged terrain chunks (design/ULTRA-GRAPHICS.md ranks 3 and 4). One spatial shader draws land and water: it reproduces the plain chunk
/// material (vertex colour times the shared grain texture) and adds, on top, a world-space noise detail and a slope-to-rock blend on land, and an animated ripple normal, a sky fresnel
/// tint and a sparse virtual sun glint on water. Water is recognised in the fragment stage as an upward-facing surface below <see cref="WaterCeiling"/> (no mesh change, so the Low and High
/// chunk meshes and their baked colours are untouched). Only <c>GraphicsPreset.ShaderTier</c> 1 and up uses it; Low and High keep the StandardMaterial3D.
/// Written against the spatial shader language that every renderer supports (compatibility included); run and tuned on an RTX 5060 Ti Forward+ (T271, T289).
/// </summary>
public static class MapShaders
{
    /// <summary>Highest top height of a water tile (<see cref="TerrainRelief.TopHeight"/> of coast and lake) plus a margin; flat land is <see cref="TerrainRelief.LandHeight"/>, so the gap between them is the water test.</summary>
    public static readonly float WaterCeiling = (TerrainRelief.TopHeight(ReliefKind.Coast) + TerrainRelief.LandHeight) / 2f;

    /// <summary>Surfaces whose world normal points up at least this much count as water or as ground (walls and steep slopes do not).</summary>
    public const float FlatNormalY = 0.9f;

    /// <summary>Name of the grain texture uniform.</summary>
    public const string GrainUniform = "grain";

    /// <summary>The shader source. Water: no time-dependent state outside <c>TIME</c>, so a still frame is a pure function of the camera and <c>TIME</c>.</summary>
    public const string TerrainUltraSource = """
        shader_type spatial;
        render_mode cull_back, diffuse_burley, specular_schlick_ggx;

        uniform sampler2D grain : source_color, filter_nearest;
        uniform float water_ceiling = 0.165;
        uniform float flat_normal_y = 0.9;
        uniform vec3 sky_tint : source_color = vec3(0.55, 0.72, 0.95);
        uniform vec3 rock_tint : source_color = vec3(0.42, 0.40, 0.38);
        uniform float detail_strength = 0.10;
        uniform float wave_speed = 0.35;
        uniform float ripple_slope = 0.8;
        uniform vec3 glint_dir = vec3(0.30, 1.0, -0.22);
        uniform float glint_threshold = 0.996;
        uniform float glint_strength = 0.3;
        uniform float glint_slope = 0.5;

        varying vec3 world_pos;
        varying vec3 world_normal;

        float hash21(vec2 p) {
            return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
        }

        float value_noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash21(i);
            float b = hash21(i + vec2(1.0, 0.0));
            float c = hash21(i + vec2(0.0, 1.0));
            float d = hash21(i + vec2(1.0, 1.0));
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }

        // Value noise with its analytic gradient: x is the value, yz the slope, so a ripple normal costs the same as one plain noise lookup.
        vec3 noised(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0); // quintic fade: a continuous slope AND curvature, so the lattice never shows in the ripple normal
            vec2 du = 30.0 * f * f * (f - 1.0) * (f - 1.0);
            float a = hash21(i);
            float b = hash21(i + vec2(1.0, 0.0));
            float c = hash21(i + vec2(0.0, 1.0));
            float d = hash21(i + vec2(1.0, 1.0));
            float k = a - b - c + d;
            return vec3(a + (b - a) * u.x + (c - a) * u.y + k * u.x * u.y, du * (vec2(b - a, c - a) + k * u.yx));
        }

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
        }

        void fragment() {
            vec3 base = COLOR.rgb * texture(grain, UV).rgb;
            bool up = normalize(world_normal).y > flat_normal_y;
            if (up && world_pos.y < water_ceiling) {
                // Water: the slope of two height-noise layers drifting in different directions tilts the normal (a ripple), the colour keeps the baked depth and shore shading.
                float t = TIME * wave_speed;
                vec3 w1 = noised(world_pos.xz * 3.2 + vec2(t, t * 0.6));
                // The second layer is rotated against the first so the two noise lattices do not line up into visible rectangles; its slope is rotated back.
                mat2 turn = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
                vec3 w2 = noised(turn * (world_pos.xz * 6.1) - vec2(t * 0.8, t));
                vec2 slope = (w1.yz * 0.65 + (w2.yz * turn) * 0.35) * ripple_slope;
                vec3 tilt = normalize(vec3(-slope.x, 1.0, -slope.y));
                NORMAL = normalize((VIEW_MATRIX * vec4(tilt, 0.0)).xyz);
                float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
                ALBEDO = mix(base, sky_tint, 0.08 + fresnel * 0.4);
                // Sun glint: the sun sits behind the camera, so a true reflection never reaches it; a fixed virtual glint direction lights the ripple crests that face it as sparkles.
                vec3 glint_n = normalize(vec3(-w1.y * glint_slope, 1.0, -w1.z * glint_slope));
                float glint = smoothstep(glint_threshold, 1.0, dot(glint_n, normalize(glint_dir)));
                EMISSION = vec3(1.0, 0.93, 0.75) * glint * glint_strength;
                ROUGHNESS = 0.12;
                SPECULAR = 0.7;
                METALLIC = 0.0;
            } else {
                // Land: world-space grain breaks up the tile-sized flat colour; steep ground (hill and mountain flanks, cliff walls) shifts toward rock.
                float detail = value_noise(world_pos.xz * 5.0) * 0.6 + value_noise(world_pos.xz * 13.0) * 0.4;
                vec3 land = base * (1.0 + (detail - 0.5) * 2.0 * detail_strength);
                float steep = 1.0 - smoothstep(0.55, 0.9, world_normal.y);
                ALBEDO = mix(land, rock_tint * (0.8 + detail * 0.4), steep * 0.55);
                ROUGHNESS = 1.0;
            }
        }
        """;

    /// <summary>Ultra's chunk material: the shader above with the same shared grain texture the plain material uses.</summary>
    public static ShaderMaterial TerrainUltra()
    {
        var material = new ShaderMaterial { Shader = new Shader { Code = TerrainUltraSource } };
        material.SetShaderParameter(GrainUniform, ProceduralAssets.ToTexture(ProceduralImages.Noise(AssetKey.Of("mesh", "terrain-chunk"))));
        material.SetShaderParameter("water_ceiling", WaterCeiling);
        material.SetShaderParameter("flat_normal_y", FlatNormalY);
        return material;
    }
}
