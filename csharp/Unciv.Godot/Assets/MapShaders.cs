using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C098: the Ultra-only shader for the merged terrain chunks (design/ULTRA-GRAPHICS.md ranks 3 and 4). One spatial shader draws land and water: it reproduces the plain chunk
/// material (vertex colour times the shared grain texture) and adds, on top, a world-space noise detail and a slope-to-rock blend on land, and an animated normal, a sky fresnel
/// tint and a sun glint on water. Water is recognised in the fragment stage as an upward-facing surface below <see cref="WaterCeiling"/> (no mesh change, so the Low and High
/// chunk meshes and their baked colours are untouched). Only <c>GraphicsPreset.ShaderTier</c> 1 and up uses it; Low and High keep the StandardMaterial3D.
/// Written against the spatial shader language that every renderer supports (compatibility included); never run on a real GPU yet, see ticket T271.
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

        void vertex() {
            world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
        }

        void fragment() {
            vec3 base = COLOR.rgb * texture(grain, UV).rgb;
            bool up = world_normal.y > flat_normal_y;
            if (up && world_pos.y < water_ceiling) {
                // Water: two noise layers drifting in different directions tilt the normal; the colour keeps the baked depth and shore shading.
                float t = TIME * wave_speed;
                vec2 p = world_pos.xz * 2.2;
                float n1 = value_noise(p + vec2(t, t * 0.6));
                float n2 = value_noise(p * 1.7 - vec2(t * 0.8, t));
                vec3 tilt = normalize(vec3((n1 - 0.5) * 0.35, 1.0, (n2 - 0.5) * 0.35));
                NORMAL = normalize((VIEW_MATRIX * vec4(tilt, 0.0)).xyz);
                float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
                ALBEDO = mix(base, sky_tint, 0.08 + fresnel * 0.4);
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
