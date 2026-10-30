using System.Globalization;
using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;

namespace Unciv.GodotHarness;

/// <summary>
/// T271: frame-time and look pass for the graphics levels on a REAL GPU. Deliberately NOT listed in <c>scenarios.txt</c> (never in CI: the numbers and pictures belong to
/// one machine, and real-GPU output must not become a golden). It ignores the seeded Tiny session it is handed and builds its own bigger game (Large map by default, ten AI civs,
/// many cities and units for the human, the whole map explored) so the map is a real one, then runs the full <see cref="GameScreen"/>.
/// Per configuration and camera view it records wall frame time (Godot's process delta, vsync off), the GPU and CPU render time the renderer measures, and a PNG.
/// Configuration through environment variables: <c>UNCIV_BENCH_OUT</c> (output folder, required), <c>UNCIV_BENCH_MODE</c> = <c>levels</c> (Low/High/Ultra, default) or
/// <c>ablation</c> (Low plus each effect alone, then everything), <c>UNCIV_BENCH_SIZE</c> (Medium/Large/Huge), <c>UNCIV_BENCH_RES</c> (WxH, default 1920x1080),
/// <c>UNCIV_BENCH_FRAMES</c> (frames per measurement, default 300), <c>UNCIV_BENCH_VIEWS</c> (comma list, default all), <c>UNCIV_BENCH_SEQ</c>=N (N extra shots 0.5 s apart in every coast view, T289), <c>UNCIV_BENCH_CLEAN</c>=1 (hide HUD, minimap, panels and yield chips, for documentation pictures). Run it with
/// <c>Unciv.Godot.Harness/tools/gpu-bench.sh</c>, which also picks the renderer and the adapter.
/// </summary>
public sealed partial class GpuBenchScenario : Scenario
{
    public override string Name => "gpu-bench";

    public override bool Uses3D => true;

    public override Vector2I Viewport => ParseRes(System.Environment.GetEnvironmentVariable("UNCIV_BENCH_RES"));

    public override IReadOnlyList<string> Checklist => ["Open the PNGs in UNCIV_BENCH_OUT: do Low, High and Ultra look right and do the colours agree?"];

    private static Vector2I ParseRes(string? text)
    {
        var parts = (text ?? "1920x1080").Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) ? new Vector2I(w, h) : new Vector2I(1920, 1080);
    }

    private sealed record Config(string Name, GraphicsPreset Preset);

    private sealed record View(string Name, float Zoom, float Tilt, Func<Vector3> Focus, bool Pan = false);

    /// <summary>Counts frames after <see cref="Begin"/>; keeps wall delta, GPU and CPU render time of every frame.</summary>
    private sealed partial class Probe : Node
    {
        public readonly List<double> Wall = [];
        public readonly List<double> Gpu = [];
        public readonly List<double> Cpu = [];
        public readonly List<double> Draws = [];
        public double ElapsedSeconds;
        public int Wanted;
        public Action? EachFrame;
        public bool Done => Wanted == 0 && Wall.Count > 0;

        public void Begin(int frames)
        {
            Wall.Clear();
            Gpu.Clear();
            Cpu.Clear();
            Draws.Clear();
            Wanted = frames;
        }

        public override void _Process(double delta)
        {
            ElapsedSeconds += delta;
            if (Wanted <= 0) return;
            var vp = GetViewport().GetViewportRid();
            Wall.Add(delta * 1000.0);
            Gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
            Cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            Draws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            EachFrame?.Invoke();
            Wanted--;
        }
    }

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string outDir = System.Environment.GetEnvironmentVariable("UNCIV_BENCH_OUT") ?? throw new InvalidOperationException("UNCIV_BENCH_OUT is not set");
        Directory.CreateDirectory(outDir);
        string mode = System.Environment.GetEnvironmentVariable("UNCIV_BENCH_MODE") ?? "levels";
        string sizeName = System.Environment.GetEnvironmentVariable("UNCIV_BENCH_SIZE") ?? "Large";
        int frames = int.TryParse(System.Environment.GetEnvironmentVariable("UNCIV_BENCH_FRAMES"), out int f) ? f : 300;
        int seqShots = int.TryParse(System.Environment.GetEnvironmentVariable("UNCIV_BENCH_SEQ"), out int sq) ? sq : 0;
        var wantedViews = (System.Environment.GetEnvironmentVariable("UNCIV_BENCH_VIEWS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

        var game = BuildGame(sizeName, out var human, out int cityCount, out int unitCount);
        var localHost = new LocalHost(new GameSession(game), ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        screen.Hud.HoldBanner = true;
        if (System.Environment.GetEnvironmentVariable("UNCIV_BENCH_CLEAN") == "1") HideHud(screen);
        var map = screen.Map;
        var probe = new Probe();
        host.AddChild(probe);
        RenderingServer.ViewportSetMeasureRenderTime(host.GetViewport().GetViewportRid(), true);

        string renderer = RenderingServer.GetCurrentRenderingMethod();
        string adapter = RenderingServer.GetVideoAdapterName();
        string info = $"renderer={renderer} adapter=\"{adapter}\" version=\"{RenderingServer.GetVideoAdapterApiVersion()}\" res={host.GetViewport().GetVisibleRect().Size} map={sizeName} tiles={game.TileMap.Values.Count} cities={cityCount} units={unitCount}";
        GD.Print("[bench] " + info);
        File.WriteAllText(Path.Combine(outDir, "info.txt"), info + "\n");
        bool forwardPlus = renderer == GraphicsModel.ForwardPlus;

        var capitalTile = human.Cities[0].Location;
        Vector3 Capital() => MapLayout.TileTop(capitalTile.X, capitalTile.Y, 0f);
        Vector3 MapCentre()
        {
            var tiles = game.TileMap.Values;
            double sx = tiles.Average(it => (double)MapLayout.TileTop(it.Position.X, it.Position.Y, 0f).X), sz = tiles.Average(it => (double)MapLayout.TileTop(it.Position.X, it.Position.Y, 0f).Z);
            var nearest = tiles.OrderBy(it => { var p = MapLayout.TileTop(it.Position.X, it.Position.Y, 0f); return (p.X - sx) * (p.X - sx) + (p.Z - sz) * (p.Z - sz); }).First();
            return MapLayout.TileTop(nearest.Position.X, nearest.Position.Y, 0f);
        }
        Vector3 Coast()
        {
            var tile = game.TileMap.Values.Where(it => it.BaseTerrain == "Coast" && it.Neighbors.Any(n => n.IsLand))
                .OrderBy(it => HexMath.GetDistance(it.Position.X, it.Position.Y, capitalTile.X, capitalTile.Y)).First();
            return MapLayout.TileTop(tile.Position.X, tile.Position.Y, 0f);
        }

        var views = new List<View>
        {
            new("overview", 40f, 55f, MapCentre),
            new("mid", 14f, 55f, Capital),
            new("close", 6f, 60f, Capital),
            new("coast", 9f, 50f, Coast),
            new("coastlow", 5f, 28f, Coast),
            new("coast30", 7f, 30f, Coast),
            new("coast55", 7f, 55f, Coast),
            new("coast75", 7f, 75f, Coast),
            new("pan", 14f, 55f, Capital, Pan: true),
        }.Where(it => wantedViews.Length == 0 || wantedViews.Contains(it.Name)).ToList();

        var configs = Configs(mode, forwardPlus);
        var resultsPath = Path.Combine(outDir, "results.csv");
        File.WriteAllText(resultsPath, "renderer,adapter,config,view,frames,wall_mean,wall_median,wall_p95,wall_max,gpu_mean,gpu_median,gpu_p95,cpu_mean,cpu_median,draw_calls\n");
        string Safe(string s) => new([.. s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_')]);
        static double Pct(List<double> v, double p) { var s = v.Order().ToList(); return s.Count == 0 ? 0 : s[Math.Min(s.Count - 1, (int)(p * s.Count))]; }
        static string N(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        void Record(string config, string view)
        {
            var wall = probe.Wall;
            var gpu = probe.Gpu;
            var cpu = probe.Cpu;
            var line = string.Join(',', renderer, "\"" + adapter + "\"", config, view, wall.Count, N(wall.Average()), N(Pct(wall, 0.5)), N(Pct(wall, 0.95)), N(wall.Max()),
                N(gpu.Average()), N(Pct(gpu, 0.5)), N(Pct(gpu, 0.95)), N(cpu.Average()), N(Pct(cpu, 0.5)), N(probe.Draws.Average()));
            File.AppendAllText(resultsPath, line + "\n");
            GD.Print($"[bench] {config,-20} {view,-9} wall mean {wall.Average():F2} med {Pct(wall, 0.5):F2} p95 {Pct(wall, 0.95):F2} max {wall.Max():F2} ms | gpu mean {gpu.Average():F2} med {Pct(gpu, 0.5):F2} | cpu mean {cpu.Average():F2}");
        }

        void Shot(string name) => ViewportCapture.CaptureViewportPng(host.GetViewport(), Path.Combine(outDir, Safe(renderer + "_" + name) + ".png"));

        var steps = new List<ScenarioStep> { new WaitFrames(30) };
        var panDirection = 1f;
        foreach (var config in configs)
        {
            foreach (var view in views)
            {
                steps.Add(new Act($"{config.Name} {view.Name}: set", () =>
                {
                    map.ApplyGraphics(config.Preset);
                    map.SetView(view.Tilt);
                    map.SetZoom(view.Zoom);
                    map.FocusOn(view.Focus());
                    probe.EachFrame = view.Pan ? () => map.PanBy(new Vector3(0.15f * panDirection, 0f, 0.05f * panDirection)) : null;
                }));
                steps.Add(new WaitFrames(45)); // shader compile, chunk culling and the glow/shadow caches settle
                steps.Add(new Act("begin", () => probe.Begin(frames)));
                steps.Add(new WaitUntil("probe done", () => probe.Done, MaxFrames: frames + 600));
                steps.Add(new Act("record", () => { Record(config.Name, view.Name); probe.EachFrame = null; }));
                if (view.Pan)
                {
                    steps.Add(new Act("pan back", () => { panDirection = -panDirection; map.FocusOn(view.Focus()); }));
                }
                else if (mode != "ablation")
                {
                    steps.Add(new Act("shot", () => Shot($"{config.Name}_{view.Name}")));
                    if (seqShots > 0 && view.Name.StartsWith("coast", StringComparison.Ordinal))
                    {
                        AddSequence(steps, probe, shotName => Shot($"{config.Name}_{view.Name}_{shotName}"), seqShots);
                    }
                    else if (view.Name is "coast" or "coastlow")
                    {
                        steps.Add(new WaitFrames(40));
                        steps.Add(new Act("shot later", () => Shot($"{config.Name}_{view.Name}_later")));
                    }
                }
            }
        }
        steps.Add(new Act("done", () => GD.Print("[bench] finished; results in " + outDir)));
        return steps;
    }

    /// <summary>T291: documentation pictures of the map itself, without the HUD, minimap, unit panel or yield chips (never used for timings).</summary>
    private static void HideHud(GameScreen screen)
    {
        screen.Hud.Visible = false;
        screen.World.Visible = false;
        screen.Minimap.Visible = false;
        screen.Panel.Visible = false;
        screen.Overlays.ShowYields = false;
    }

    /// <summary>T289: a short animation sequence, one shot every half second of rendered time (the water animates with TIME); the clock is the sum of frame deltas.</summary>
    private static void AddSequence(List<ScenarioStep> steps, Probe probe, Action<string> shot, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string name = $"t{i}";
            double start = 0;
            steps.Add(new Act("seq wait", () => start = probe.ElapsedSeconds));
            steps.Add(new WaitUntil("half second", () => probe.ElapsedSeconds - start >= 0.5, MaxFrames: 100000));
            steps.Add(new Act("seq shot", () => shot(name)));
        }
    }

    /// <summary>The configurations to measure: Low, High and Ultra, or (ablation) Low plus each effect alone first; Ultra loses SSAO and SSR on the compatibility renderer, which errors on them.</summary>
    private static List<Config> Configs(string mode, bool forwardPlus)
    {
        var low = GraphicsModel.Preset(GraphicsQuality.Low);
        var high = GraphicsModel.Preset(GraphicsQuality.High);
        var ultra = GraphicsModel.Preset(GraphicsQuality.Ultra);
        if (!forwardPlus) ultra = ultra with { Ssao = false, Ssr = false }; // errors on the compatibility renderer; labelled below
        // Calibration sweeps: UNCIV_BENCH_ULTRA="exposure,ambient,saturation" overrides those constants of the Ultra preset (and of nothing else)
        if ((System.Environment.GetEnvironmentVariable("UNCIV_BENCH_ULTRA") ?? "").Split(',') is [var expo, var amb, var sat] && float.TryParse(expo, CultureInfo.InvariantCulture, out float e) && float.TryParse(amb, CultureInfo.InvariantCulture, out float a) && float.TryParse(sat, CultureInfo.InvariantCulture, out float sa))
        {
            ultra = ultra with { Exposure = e, AmbientEnergy = a, Saturation = sa };
        }
        var onlyConfig = System.Environment.GetEnvironmentVariable("UNCIV_BENCH_CONFIGS");
        string ultraName = forwardPlus ? "Ultra" : "Ultra-no-ssao-ssr";
        var configs = new List<Config>();
        if (mode == "ablation")
        {
            configs.Add(new("Low", low));
            configs.Add(new("+shadows", low with { Shadows = true }));
            configs.Add(new("+tonemap", low with { Tonemap = true }));
            configs.Add(new("+glow", low with { Glow = true }));
            configs.Add(new("+msaa2", low with { Msaa = 2 }));
            configs.Add(new("+msaa4", low with { Msaa = 4 }));
            configs.Add(new("+shader", low with { ShaderTier = 1 }));
            if (forwardPlus)
            {
                configs.Add(new("+ssao", low with { Ssao = true }));
                configs.Add(new("+ssr", low with { Ssr = true }));
            }
            configs.Add(new("High", high));
            configs.Add(new(ultraName, ultra));
        }
        else
        {
            configs.Add(new("Low", low));
            configs.Add(new("High", high));
            configs.Add(new(ultraName, ultra));
        }

        return string.IsNullOrEmpty(onlyConfig) ? configs : configs.Where(it => onlyConfig.Split(',').Contains(it.Name)).ToList();
    }

    /// <summary>A Large (by default) map with the human and ten AI civs: every civ founds its capital, the human gets a spread of further cities and units, and the human knows the whole map.</summary>
    private static GameInfo BuildGame(string sizeName, out Logic.Civilization.Civilization human, out int cityCount, out int unitCount)
    {
        var vanilla = ScenarioGame.LoadVanilla();
        var size = sizeName switch { "Medium" => MapSize.Medium, "Huge" => MapSize.Huge, _ => MapSize.Large };
        var players = new List<Player> { new Player(ScenarioGame.HumanA, PlayerType.Human), new Player(ScenarioGame.Ai) };
        players.AddRange(vanilla.Nations.Values.Where(it => it.IsMajorCiv && it.Name != ScenarioGame.HumanA && it.Name != ScenarioGame.Ai).Select(it => it.Name).Order(StringComparer.Ordinal)
            .Take(ScenarioGame.CrowdAiCount - 1).Select(it => new Player(it)));
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters { BaseRuleset = vanilla.Name, NumberOfCityStates = 4, VictoryTypes = vanilla.Victories.Keys.ToList(), Players = players },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = ScenarioGame.MapSeed, MapSize = size }),
            ScenarioGame.GameSeed, ScenarioGame.FixedClockMillis);
        cityCount = 0;
        unitCount = 0;
        var cityTiles = new List<HexCoord>();
        foreach (var civ in game.Civilizations.Where(it => it.IsMajorCiv()).OrderBy(it => it.CivName, StringComparer.Ordinal))
        {
            var settler = civ.Units.GetCivUnits().FirstOrDefault(it => it.BaseUnit.IsCityFounder());
            if (settler == null) continue;
            cityTiles.Add(settler.CurrentTile.Position);
            civ.AddCity(settler.CurrentTile.Position, settler);
            cityCount++;
        }
        human = game.GetCivilization(ScenarioGame.HumanA);
        var capital = human.Cities[0].Location;
        var sites = game.TileMap.Values
            .Where(it => it.IsLand && !it.IsImpassible() && HexMath.GetDistance(it.Position.X, it.Position.Y, capital.X, capital.Y) <= 24)
            .OrderBy(it => HexMath.GetDistance(it.Position.X, it.Position.Y, capital.X, capital.Y)).ThenBy(it => it.Position.X).ThenBy(it => it.Position.Y).ToList();
        foreach (var tile in sites)
        {
            if (human.Cities.Count >= 14) break;
            if (cityTiles.Any(c => HexMath.GetDistance(c.X, c.Y, tile.Position.X, tile.Position.Y) < 4)) continue;
            cityTiles.Add(tile.Position);
            human.AddCity(tile.Position);
            cityCount++;
        }
        string[] kinds = ["Warrior", "Archer", "Horseman", "Spearman", "Scout", "Worker", "Swordsman", "Catapult"];
        foreach (var city in human.Cities.ToList())
        {
            for (int i = 0; i < 6; i++)
            {
                if (human.Units.PlaceUnitNearTile(city.Location, kinds[(i + city.Location.X) % kinds.Length]) != null) unitCount++;
            }
        }
        foreach (var tile in game.TileMap.Values) tile.SetExplored(human, true);
        human.Cache.UpdateViewableTiles();
        return game;
    }
}
