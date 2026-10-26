using Godot;
using Unciv.Assets;
using Unciv.Models;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// The only way client code obtains a mesh, material, texture, icon, font, theme, style box or model: <c>Get&lt;T&gt;(key)</c>.
/// Resolution (see <see cref="AssetResolver"/>): override file, then selected pack, along the key's fallback chain, then the procedural generator.
/// Get never returns null and never throws for a well-formed key and a type the key's kind can produce
/// (Mesh; Material or Texture2D for terrain; Node3D for unit and prop keys; Texture2D for icons; Theme; StyleBox; Font; AudioStream for sound keys, see <see cref="Sound"/>).
/// Only the first file found along the chain is tried; if it cannot be loaded as T the placeholder is used.
/// Shared resources are cached per key; Node3D models are built fresh on every call because the caller owns and parents them.
/// </summary>
public sealed class AssetRegistry
{
    private readonly AssetResolver resolver;
    private readonly IAssetFallbacks fallbacks;
    private readonly Dictionary<(AssetKey, Type), object> cache = [];

    public AssetRegistry(IAssetFiles files, IAssetFallbacks fallbacks, string? pack = null)
    {
        this.fallbacks = fallbacks;
        resolver = new AssetResolver(files, fallbacks, pack);
    }

    public static AssetRegistry ForRuleset(RulesetClass ruleset, string? pack = null) =>
        new(new GodotAssetFiles(), RulesetAssetKeys.FallbacksFor(ruleset), pack);

    public IReadOnlyList<string> Diagnostics => resolver.Diagnostics;

    public AssetResolution Resolve(AssetKey key) => resolver.Resolve(key);

    /// <exception cref="ArgumentException">The key's kind cannot produce <typeparamref name="T"/> (a programming error).</exception>
    public T Get<T>(AssetKey key) where T : class
    {
        if (cache.TryGetValue((key, typeof(T)), out var cached)) return (T)cached;
        var resolution = resolver.Resolve(key);
        T? result = null;
        if (resolution.Path != null) result = LoadFile<T>(resolution.Path);
        result ??= Generate<T>(key, UnitTypeOf(key));
        if (result is not Node3D) cache[(key, typeof(T))] = result;
        return result;
    }

    /// <summary>The stream of a sound (override <c>sound/&lt;name&gt;.ogg</c> or the generated tones), null for <see cref="UncivSound.Silent"/>.</summary>
    public AudioStream? Sound(UncivSound sound) => ProceduralSounds.KeyFor(sound) is { } key ? Get<AudioStream>(key) : null;

    private static T? LoadFile<T>(string path) where T : class
    {
        var loaded = GodotAssetFiles.Load(path);
        if (loaded is T direct) return direct;
        if (loaded is PackedScene scene && scene.Instantiate() is T instance) return instance;
        if (loaded != null) GD.PushWarning($"Asset file {path} is a {loaded.GetType().Name}, not usable as {typeof(T).Name}; using the placeholder");
        return null;
    }

    /// <summary>The unit type a unit key falls back to (first unitType entry of its chain), used to pick the placeholder shape.</summary>
    private string? UnitTypeOf(AssetKey key) =>
        AssetFallbacks.Chain(key, fallbacks).Where(k => k.Category == RulesetAssetKeys.UnitType).Select(k => k.Name).FirstOrDefault();

    private static T Generate<T>(AssetKey key, string? unitType) where T : class
    {
        object? made = AssetKinds.Of(key) switch
        {
            AssetKind.TerrainMaterial when typeof(T).IsAssignableFrom(typeof(StandardMaterial3D)) => ProceduralAssets.TerrainMaterial(key),
            AssetKind.TerrainMaterial when typeof(T).IsAssignableFrom(typeof(ImageTexture)) => ProceduralAssets.ToTexture(ProceduralImages.Noise(key)),
            AssetKind.Mesh => ProceduralAssets.MeshFor(key),
            AssetKind.UnitBlock => ProceduralAssets.Model(key, ModelSpecs.Unit(key, unitType), withBanner: true),
            AssetKind.Prop => ProceduralAssets.Model(key, ModelSpecs.Prop(key), withBanner: false),
            AssetKind.Icon => ProceduralAssets.Icon(key),
            AssetKind.Theme => ProceduralAssets.BuildTheme(),
            AssetKind.StyleBox => key.Name == "button" ? ProceduralAssets.Button() : ProceduralAssets.Panel(),
            AssetKind.Font => FontChain.Default,
            AssetKind.Sound => ProceduralAssets.Sound(key),
            _ => null,
        };
        return made as T ?? throw new ArgumentException($"{key} is a {AssetKinds.Of(key)} asset and cannot be obtained as {typeof(T).Name}");
    }
}
