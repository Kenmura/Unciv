using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Settings;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C098: the Ultra water and terrain shader on the terrain-3d gallery. Deliberately NOT listed in <c>scenarios.txt</c> (so CI never runs it) and with no golden: the shader
/// animates with time and the look is only meaningful on a real GPU. It asserts the plumbing (Low and High keep the plain material, Ultra swaps in the shader and Low swaps it
/// back, the shader declares the uniforms the code sets) and writes <c>ultra-water.png</c> / <c>ultra-low.png</c> next to the other outputs for a human to look at.
/// Run it with <c>tools/run-scenario.sh terrain-ultra</c>; on this machine that renders on llvmpipe OpenGL, which proves only that the shader compiles and draws there.
/// </summary>
public sealed class TerrainUltraScenario : Scenario
{
    public override string Name => "terrain-ultra";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the water show soft moving ripples and a sky tint rather than flat blue, and do the hills look rockier than the plains?",
        "Is the Ultra picture still the same palette as Low (no wash-out or crushed colours)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var view = new MapView();
        host.AddChild(view);
        view.Load(AssetRegistry.ForRuleset(session.Game.Ruleset), Terrain3DScenario.Gallery());
        view.SetView(60f);
        view.SetZoom(10f);
        string outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-terrain-ultra-" + System.Environment.ProcessId);
        Directory.CreateDirectory(outDir);

        void Save(string name) => ViewportCapture.CaptureViewportPng(host.GetViewport(), System.IO.Path.Combine(outDir, name));
        ShaderMaterial? Shader() => view.TerrainMaterial as ShaderMaterial;

        return
        [
            new WaitFrames(3),
            new AssertQuery("Low draws the plain chunk material", () => view.TerrainMaterial is StandardMaterial3D),
            new Act("apply High", () => view.ApplyGraphics(GraphicsModel.Preset(GraphicsQuality.High))),
            new AssertQuery("High still draws the plain chunk material (shader polish is Ultra only)", () => view.TerrainMaterial is StandardMaterial3D),
            new Act("apply Ultra without the Forward+ only effects (SSAO and SSR error on the compatibility renderer this scenario runs on)", () => view.ApplyGraphics(GraphicsModel.Preset(GraphicsQuality.Ultra) with { Ssao = false, Ssr = false })),
            new WaitFrames(6),
            new AssertQuery("Ultra draws the shader material", () => Shader() is { Shader: not null }),
            new AssertQuery("the shader declares the uniforms the code sets", () =>
            {
                var names = Shader().NotNull().Shader.GetShaderUniformList().Select(it => it.AsGodotDictionary()["name"].AsString()).ToList();
                return new[] { MapShaders.GrainUniform, "water_ceiling", "flat_normal_y" }.All(names.Contains);
            }),
            new AssertQuery("the grain texture and water ceiling are set", () => Shader().NotNull().GetShaderParameter(MapShaders.GrainUniform).Obj is Texture2D
                && Math.Abs((float)Shader().NotNull().GetShaderParameter("water_ceiling") - MapShaders.WaterCeiling) < 1e-6f),
            new Act("save the Ultra picture", () => { Save("ultra-water.png"); GD.Print("[terrain-ultra] pictures in " + outDir); }),
            new Act("back to Low", () => view.ApplyGraphics(GraphicsModel.Preset(GraphicsQuality.Low))),
            new WaitFrames(3),
            new AssertQuery("Low goes back to the plain chunk material", () => view.TerrainMaterial is StandardMaterial3D),
            new Act("save the Low picture", () => Save("ultra-low.png")),
        ];
    }
}
