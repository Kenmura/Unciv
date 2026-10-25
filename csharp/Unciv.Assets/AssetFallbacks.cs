namespace Unciv.Assets;

/// <summary>Maps a key to the next more general key (e.g. <c>unit/Warrior</c> to <c>unitType/Melee</c>). Absent means "go straight to the category default".</summary>
public interface IAssetFallbacks
{
    AssetKey? ParentOf(AssetKey key);
}

public sealed class AssetFallbacks : IAssetFallbacks
{
    public static AssetFallbacks None { get; } = new();

    private readonly Dictionary<AssetKey, AssetKey> parents = [];

    public AssetFallbacks Add(AssetKey key, AssetKey parent)
    {
        parents[key] = parent;
        return this;
    }

    public AssetKey? ParentOf(AssetKey key) => parents.TryGetValue(key, out var parent) ? parent : null;

    /// <summary>
    /// The ordered candidates for <paramref name="key"/>: the key, its parents (cycle-safe, at most 8 hops), then <c>category/_default</c> of the
    /// original key's category. Never empty and always ends in a default key.
    /// </summary>
    public static IReadOnlyList<AssetKey> Chain(AssetKey key, IAssetFallbacks fallbacks)
    {
        var chain = new List<AssetKey> { key };
        var current = key;
        for (var hops = 0; hops < 8; hops++)
        {
            var parent = fallbacks.ParentOf(current);
            if (parent is not { } next || chain.Contains(next)) break;
            chain.Add(next);
            current = next;
        }
        var fallback = key.Default;
        if (!chain.Contains(fallback)) chain.Add(fallback);
        return chain;
    }
}
