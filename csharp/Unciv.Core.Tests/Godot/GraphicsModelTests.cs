using Unciv.Assets;
using Unciv.GodotClient.Settings;

namespace Unciv.Tests.Godot;

// C097: the Godot-free graphics rules: preset table, effective level, the restart flow with its loop guard, and the persisted fields.
public sealed class GraphicsModelTests : IDisposable
{
    private const string Compat = GraphicsModel.Compatibility;
    private const string Plus = GraphicsModel.ForwardPlus;
    private const string Gpu = "NVIDIA GeForce RTX 3060";
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-graphics-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, true); }
        catch (IOException) { }
    }

    private ClientSettings LoadText(string text)
    {
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, text);
        return new ClientSettingsStore(file).Load();
    }

    [Fact]
    public void LowIsTodaysLookExactly()
    {
        var low = GraphicsModel.Preset(GraphicsQuality.Low);
        Assert.False(low.Shadows || low.Tonemap || low.Glow || low.Ssao || low.Ssr);
        Assert.Equal(0, low.Msaa);
        Assert.Equal(1f, low.Exposure);
        Assert.Equal(0.55f, low.AmbientEnergy); // MapMaterials.Backdrop
        Assert.Equal(UnitLod.FarZoom, low.FarZoom); // the C085 caps are unchanged at Low
    }

    [Fact]
    public void HighIsCompatibilitySafe()
    {
        var high = GraphicsModel.Preset(GraphicsQuality.High);
        Assert.True(high.Shadows && high.Tonemap && high.Glow);
        Assert.Equal(2, high.Msaa);
        Assert.False(high.Ssao || high.Ssr); // Forward+ only
        Assert.Equal(Compat, GraphicsModel.RendererFor(GraphicsQuality.High));
    }

    [Fact]
    public void UltraAddsTheForwardPlusEffects()
    {
        var ultra = GraphicsModel.Preset(GraphicsQuality.Ultra);
        Assert.True(ultra.Shadows && ultra.Tonemap && ultra.Glow && ultra.Ssao && ultra.Ssr);
        Assert.Equal(4, ultra.Msaa);
        Assert.Equal(Plus, GraphicsModel.RendererFor(GraphicsQuality.Ultra));
    }

    [Fact]
    public void UltraIsCalibratedDarkerAndRicherThanForwardPlusDefaults()
    {
        // T271: measured on a real GPU, Forward+ with High's constants came out about 40% brighter and paler than the compatibility High; Ultra compensates
        var high = GraphicsModel.Preset(GraphicsQuality.High);
        var ultra = GraphicsModel.Preset(GraphicsQuality.Ultra);
        Assert.True(ultra.Exposure < high.Exposure);
        Assert.True(ultra.AmbientEnergy < high.AmbientEnergy);
        Assert.True(ultra.Saturation > 1f);
        Assert.Equal(1f, GraphicsModel.Preset(GraphicsQuality.Low).Saturation); // Low stays the identity look
        Assert.Equal(1f, high.Saturation);
    }

    [Fact]
    public void OnlyUltraDrawsTheShaderPolish() => Assert.All(Enum.GetValues<GraphicsQuality>(), it => Assert.Equal(it == GraphicsQuality.Ultra ? 1 : 0, GraphicsModel.Preset(it).ShaderTier));

    [Fact]
    public void HigherLevelsLoosenTheLodCaps()
    {
        float low = GraphicsModel.Preset(GraphicsQuality.Low).FarZoom, high = GraphicsModel.Preset(GraphicsQuality.High).FarZoom, ultra = GraphicsModel.Preset(GraphicsQuality.Ultra).FarZoom;
        Assert.True(low < high && high < ultra);
        Assert.True(UnitLod.IsFar(24f, low));
        Assert.False(UnitLod.IsFar(24f, high));
        Assert.True(ultra <= 40f); // the farthest camera (MapView.MaxZoom) still gets the reduced level
    }

    [Theory]
    [InlineData("Low", GraphicsQuality.Low)]
    [InlineData("High", GraphicsQuality.High)]
    [InlineData("Ultra", GraphicsQuality.Ultra)]
    [InlineData("ultra", GraphicsQuality.Low)]
    [InlineData("7", GraphicsQuality.Low)]
    [InlineData("Extreme", GraphicsQuality.Low)]
    [InlineData("", GraphicsQuality.Low)]
    [InlineData(null, GraphicsQuality.Low)]
    public void ParseKnowsOnlyTheThreeNames(string? name, GraphicsQuality expected) => Assert.Equal(expected, GraphicsModel.Parse(name));

    [Theory]
    [InlineData("llvmpipe (LLVM 19.1.7, 256 bits)", true)]
    [InlineData("Mesa lavapipe", true)]
    [InlineData("SwiftShader Device", true)]
    [InlineData(Gpu, false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SoftwareAdaptersAreRecognised(string? name, bool software) => Assert.Equal(software, GraphicsModel.IsSoftwareAdapter(name));

    [Theory]
    [InlineData(GraphicsQuality.Low, false, Compat, GraphicsQuality.Low)]
    [InlineData(GraphicsQuality.High, false, Compat, GraphicsQuality.High)]
    [InlineData(GraphicsQuality.Ultra, false, Plus, GraphicsQuality.Ultra)]
    [InlineData(GraphicsQuality.Ultra, false, Compat, GraphicsQuality.High)]
    [InlineData(GraphicsQuality.Ultra, false, "mobile", GraphicsQuality.High)]
    [InlineData(GraphicsQuality.Ultra, true, Plus, GraphicsQuality.High)]
    [InlineData(GraphicsQuality.Low, true, Plus, GraphicsQuality.Low)]
    public void EffectiveLevelFallsBackToHigh(GraphicsQuality requested, bool failed, string renderer, GraphicsQuality expected) =>
        Assert.Equal(expected, GraphicsModel.Effective(requested, failed, renderer, Gpu));

    [Fact]
    public void UltraOnASoftwareAdapterShowsHigh() => Assert.Equal(GraphicsQuality.High, GraphicsModel.Effective(GraphicsQuality.Ultra, false, Plus, "llvmpipe"));

    [Fact]
    public void FirstStartAtLowDoesNothing()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.Low, false, "", Compat, Gpu);
        Assert.Equal(new GraphicsStartup(GraphicsQuality.Low, null, "", false), start);
    }

    [Fact]
    public void UltraOnCompatibilityStartsOneRestartAndStoresTheMarker()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.Ultra, false, "", Compat, Gpu);
        Assert.Equal(Plus, start.RestartInto);
        Assert.Equal(Plus, start.Pending);
        Assert.False(start.UltraFailed);
        Assert.Equal(GraphicsQuality.High, start.Effective);
    }

    [Fact]
    public void SuccessfulRestartClearsTheMarker()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.Ultra, false, Plus, Plus, Gpu);
        Assert.Equal(new GraphicsStartup(GraphicsQuality.Ultra, null, "", false), start);
    }

    [Fact]
    public void RestartThatDidNotReachForwardPlusFailsOnceAndNeverLoops()
    {
        var first = GraphicsModel.Startup(GraphicsQuality.Ultra, false, Plus, Compat, Gpu);
        Assert.Null(first.RestartInto);
        Assert.True(first.UltraFailed);
        Assert.Equal("", first.Pending);
        Assert.Equal(GraphicsQuality.High, first.Effective);
        // whatever follows, the failure flag stops the restart
        var again = GraphicsModel.Startup(GraphicsQuality.Ultra, first.UltraFailed, first.Pending, Compat, Gpu);
        Assert.Null(again.RestartInto);
        Assert.True(again.UltraFailed);
    }

    [Fact]
    public void ForwardPlusOnASoftwareAdapterIsAFailedAttempt()
    {
        var pendingCase = GraphicsModel.Startup(GraphicsQuality.Ultra, false, Plus, Plus, "llvmpipe");
        Assert.True(pendingCase.UltraFailed);
        Assert.Equal(GraphicsQuality.High, pendingCase.Effective);
        var directCase = GraphicsModel.Startup(GraphicsQuality.Ultra, false, "", Plus, "lavapipe");
        Assert.True(directCase.UltraFailed);
    }

    [Fact]
    public void LeavingForwardPlusForALowerLevelRestartsBackOnce()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.High, false, "", Plus, Gpu);
        Assert.Equal(Compat, start.RestartInto);
        Assert.Equal(Compat, start.Pending);
        var after = GraphicsModel.Startup(GraphicsQuality.High, false, Compat, Compat, Gpu);
        Assert.Null(after.RestartInto);
        Assert.Equal("", after.Pending);
        // a marker that cannot be honoured is dropped without another attempt and without blaming Ultra
        var refused = GraphicsModel.Startup(GraphicsQuality.High, false, Compat, Plus, Gpu);
        Assert.Null(refused.RestartInto);
        Assert.False(refused.UltraFailed);
    }

    [Fact]
    public void LowAndHighRunOnAnyNonForwardPlusRenderer()
    {
        Assert.Null(GraphicsModel.Startup(GraphicsQuality.High, false, "", "mobile", Gpu).RestartInto);
        Assert.Null(GraphicsModel.Startup(GraphicsQuality.Low, false, "", "mobile", Gpu).RestartInto);
    }

    [Fact]
    public void FailedUltraOnForwardPlusReturnsToCompatibility()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.Ultra, true, "", Plus, Gpu);
        Assert.Equal(GraphicsQuality.High, start.Effective);
        Assert.Equal(Compat, start.RestartInto);
    }

    [Theory]
    [InlineData(GraphicsQuality.Low, Compat, false)]
    [InlineData(GraphicsQuality.Ultra, Compat, true)]
    [InlineData(GraphicsQuality.Ultra, Plus, false)]
    [InlineData(GraphicsQuality.High, Plus, true)]
    public void NeedsRestartMatchesTheStartFlow(GraphicsQuality requested, string renderer, bool needs) =>
        Assert.Equal(needs, GraphicsModel.NeedsRestart(requested, false, renderer, Gpu));

    [Fact]
    public void FailedUltraOnSoftwareForwardPlusReturnsToCompatibility()
    {
        var start = GraphicsModel.Startup(GraphicsQuality.Ultra, false, "", Plus, "llvmpipe");
        Assert.Equal(Compat, start.RestartInto);
        Assert.True(start.UltraFailed);
        Assert.Equal(GraphicsQuality.High, start.Effective);
    }

    [Fact]
    public void StaleOrUnknownMarkerNeverRestarts()
    {
        Assert.Null(GraphicsModel.Startup(GraphicsQuality.Ultra, false, "mobile", Compat, Gpu).RestartInto);
        Assert.Null(GraphicsModel.Startup(GraphicsQuality.High, false, "mobile", "mobile", Gpu).RestartInto);
        Assert.Null(GraphicsModel.Startup(GraphicsQuality.Ultra, false, null!, Plus, Gpu).RestartInto);
    }

    [Fact]
    public void LowAmbientIsTheBackdropsConstant() => Assert.Equal(GraphicsModel.LowAmbientEnergy, GraphicsModel.Preset(GraphicsQuality.Low).AmbientEnergy);

    [Fact]
    public void NoteSaysWhatIsHappening()
    {
        Assert.Equal("", GraphicsModel.Note(GraphicsQuality.Low, false, Compat, Gpu));
        Assert.Equal("", GraphicsModel.Note(GraphicsQuality.High, false, Compat, Gpu));
        Assert.Equal("", GraphicsModel.Note(GraphicsQuality.Ultra, false, Plus, Gpu));
        Assert.Equal("Ultra needs a restart to switch the renderer", GraphicsModel.Note(GraphicsQuality.Ultra, false, Compat, Gpu));
        Assert.Equal("Ultra needs a Vulkan or Direct3D 12 GPU; showing High", GraphicsModel.Note(GraphicsQuality.Ultra, true, Compat, Gpu));
        Assert.Equal("Ultra needs a Vulkan or Direct3D 12 GPU; showing High", GraphicsModel.Note(GraphicsQuality.Ultra, false, Plus, "llvmpipe"));
        Assert.Equal("Restart to return to the standard renderer", GraphicsModel.Note(GraphicsQuality.High, false, Plus, Gpu));
    }

    [Theory]
    [InlineData("Intel(R) Graphics (ARL)", true)]
    [InlineData("Mesa Intel(R) Graphics (ARL)", true)]
    [InlineData("Intel(R) UHD Graphics 630", true)]
    [InlineData("AMD Radeon(TM) Graphics", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", false)]
    [InlineData("Intel(R) Arc(TM) Graphics", true)]
    [InlineData("AMD Radeon(TM) 780M Graphics", true)]
    [InlineData("AMD Radeon(TM) Vega 8 Graphics", true)]
    [InlineData("AMD Radeon RX 9070 XT", false)]
    [InlineData("AMD Radeon Pro W7800", false)]
    [InlineData("NVIDIA GeForce RTX 5060 Ti", false)]
    [InlineData("AMD Radeon RX 7800 XT", false)]
    [InlineData("llvmpipe (LLVM 22.1.8, 256 bits)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IntegratedAdapterNames(string? name, bool expected) => Assert.Equal(expected, GraphicsModel.IsIntegratedAdapterName(name));

    [Fact]
    public void DeviceTypeWinsOverTheName()
    {
        Assert.Equal("", GraphicsModel.IntegratedNote(GraphicsQuality.Low));
        Assert.True(GraphicsModel.IsIntegrated(true, Gpu));
        Assert.False(GraphicsModel.IsIntegrated(false, "Intel(R) Graphics (ARL)"));
        Assert.True(GraphicsModel.IsIntegrated(null, "Intel(R) Graphics (ARL)"));
        Assert.False(GraphicsModel.IsIntegrated(null, Gpu));
    }

    [Fact]
    public void IntegratedNoteIsAdviceForHighAndUltraOnly()
    {
        Assert.Equal("", GraphicsModel.Note(GraphicsQuality.Low, false, Compat, Gpu, integrated: true));
        Assert.Equal("High may run slowly on integrated graphics; Low is the smoothest", GraphicsModel.Note(GraphicsQuality.High, false, Compat, Gpu, integrated: true));
        Assert.Equal("Ultra may run slowly on integrated graphics; Low is the smoothest", GraphicsModel.Note(GraphicsQuality.Ultra, false, Plus, Gpu, integrated: true));
        Assert.Equal("", GraphicsModel.Note(GraphicsQuality.High, false, Compat, Gpu, integrated: false));
        // the restart and failure notes keep priority, and the note never changes what is drawn
        Assert.Equal("Ultra needs a restart to switch the renderer", GraphicsModel.Note(GraphicsQuality.Ultra, false, Compat, Gpu, integrated: true));
        Assert.Equal("Ultra needs a Vulkan or Direct3D 12 GPU; showing High", GraphicsModel.Note(GraphicsQuality.Ultra, true, Compat, Gpu, integrated: true));
        Assert.Equal("Restart to return to the standard renderer", GraphicsModel.Note(GraphicsQuality.High, false, Plus, Gpu, integrated: true));
        Assert.Equal(GraphicsQuality.Ultra, GraphicsModel.Effective(GraphicsQuality.Ultra, false, Plus, "Intel(R) Graphics (ARL)"));
    }

    [Fact]
    public void RestartArgsReplaceEarlierRenderingChoices()
    {
        var args = GraphicsModel.RestartArgs(["--path", "game", "--rendering-method", "mobile", "--rendering-driver=vulkan", "--verbose", "--", "--exit-on-menu"], Plus);
        Assert.Equal(["--path", "game", "--verbose", "--rendering-method", Plus, "--", "--exit-on-menu"], args);
        Assert.Equal(["--rendering-method", Compat], GraphicsModel.RestartArgs([], Compat));
        Assert.Equal(["--x", "--rendering-method", Compat], GraphicsModel.RestartArgs(["--x", "--rendering-method=forward_plus"], Compat));
        Assert.Equal(["--rendering-method", Plus, "--", "--rendering-method", "keep"], GraphicsModel.RestartArgs(["--", "--rendering-method", "keep"], Plus)); // the game's own arguments are not touched
    }

    // settings file

    [Fact]
    public void DefaultsAreLowWithNoFailure()
    {
        var settings = ClientSettings.Default;
        Assert.Equal(GraphicsQuality.Low, settings.Quality);
        Assert.False(settings.UltraFailed);
        Assert.Equal("", settings.RendererPending);
    }

    [Fact]
    public void VersionOneFileMigratesToLow()
    {
        var loaded = LoadText("{\"version\":1,\"language\":\"German\"}");
        Assert.Equal(GraphicsQuality.Low, loaded.Quality);
        Assert.Equal("German", loaded.Language);
    }

    [Fact]
    public void GraphicsFieldsRoundTripAndTheFileIsStampedWithTheCurrentVersion()
    {
        string file = Path.Combine(dir, "settings.json");
        var store = new ClientSettingsStore(file);
        Assert.True(store.Save(ClientSettings.Default with { GraphicsLevel = "Ultra", UltraFailed = true, RendererPending = Plus }));
        Assert.Contains("\"graphicsQuality\": \"Ultra\"", File.ReadAllText(file), StringComparison.Ordinal);
        var loaded = new ClientSettingsStore(file).Load();
        Assert.Equal(GraphicsQuality.Ultra, loaded.Quality);
        Assert.True(loaded.UltraFailed);
        Assert.Equal(Plus, loaded.RendererPending);
        Assert.Equal(ClientSettings.CurrentVersion, loaded.Version);
    }

    [Theory]
    [InlineData("{\"version\":2,\"graphicsQuality\":\"Extreme\"}")]
    [InlineData("{\"version\":2,\"graphicsQuality\":null}")]
    [InlineData("{\"version\":2,\"graphicsQuality\":\"ultra\"}")]
    public void HandEditedLevelsBecomeLow(string text) => Assert.Equal(GraphicsQuality.Low, LoadText(text).Quality);

    [Theory]
    [InlineData("forward_plus", "forward_plus")]
    [InlineData("gl_compatibility", "gl_compatibility")]
    [InlineData("vulkan", "")]
    [InlineData(null, "")]
    public void OnlyKnownRestartMarkersSurvive(string? marker, string expected)
    {
        string json = marker == null ? "null" : "\"" + marker + "\"";
        Assert.Equal(expected, LoadText("{\"version\":2,\"rendererPending\":" + json + "}").RendererPending);
    }

    [Fact]
    public void ServiceKeepsAndPersistsTheLevel()
    {
        string file = Path.Combine(dir, "settings.json");
        var service = new ClientSettingsService(new ClientSettingsStore(file));
        Assert.True(service.Update(s => s with { GraphicsLevel = "High" }));
        Assert.Equal(GraphicsQuality.High, new ClientSettingsService(new ClientSettingsStore(file)).Current.Quality);
    }
}
