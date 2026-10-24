using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;

namespace Unciv.Logic.Files;

// core/src/com/unciv/logic/files/IMediaFinder.kt
//
// Deviations:
// - Everything Kotlin reads from Gdx / UncivGame.Current (internal assets, mod folders, settings.visualMods, running game ruleset,
//   desktop-jar / Android checks) is the explicit IMediaEnvironment returned by `Environment`; GetRuleset() defaults to null
//   (no game loaded) and is overridden by hosts that have a game.
// - Kotlin's `super.listMediaFolders()` (LabeledSounds.cacheMods) is MediaFinderDefaults.ListMediaFolders, since C# cannot call a base interface default.
// - Kotlin's `IMediaFinder by Sounds()` delegation is plain composition: hold a Sounds and forward.
// - Char.titlecase() is char.ToUpperInvariant (identical for the ASCII sound names involved).

/// <summary>What <see cref="IMediaFinder"/> needs from the host: Gdx.files.internal, UncivGame.Current.files/settings and platform checks.</summary>
public interface IMediaEnvironment
{
    /// <summary><c>Gdx.files.internal(path)</c></summary>
    IUncivFileHandle GetInternal(string path);

    /// <summary><c>UncivGame.Current.files.getModFolder(modName)</c></summary>
    IUncivFileHandle GetModFolder(string modName);

    /// <summary><c>UncivGame.Current.settings.visualMods</c>; empty when no UncivGame is initialized.</summary>
    IEnumerable<string> VisualMods { get; }

    /// <summary><c>UncivGame.isCurrentInitialized()</c></summary>
    bool IsGameInitialized { get; }

    /// <summary><c>Gdx.app.type == Android</c></summary>
    bool IsAndroid { get; }

    /// <summary><c>Gdx.app.type == Desktop &amp;&amp; isRunFromJar</c></summary>
    bool IsRunFromJar { get; }
}

/// <summary>
/// Encapsulate how media files are found and enumerated: map a name to a file, enumerate folders and files over all active mods
/// (mods of the running game first, then permanent audiovisual mods, then builtin).
/// </summary>
public interface IMediaFinder
{
    IMediaEnvironment Environment { get; }

    /// <summary>Set of supported extensions <b>including the leading dot</b>. <c>{""}</c> = full names only; empty set = <see cref="FindMedia"/> always returns null.</summary>
    IReadOnlySet<string> SupportedMediaExtensions { get; }

    /// <summary>Name of assets subfolder, a direct child of a Mod folder or the internal assets folder.</summary>
    string MediaSubFolderName { get; }

    /// <summary>The current Ruleset, or <c>null</c> if no game is loaded - only permanent audiovisual mods and builtin are valid sources.</summary>
    Ruleset? GetRuleset() => null;

    /// <summary>Possible file names for the builtin folder (its children cannot be enumerated from a jar). <see cref="ListMediaFiles"/> throws unless overridden.</summary>
    IEnumerable<string> GetInternalMediaNames(IUncivFileHandle folder) =>
        // JUSTIFY: T118 Kotlin default throws unless overridden (permanent)
        throw new NotSupportedException("Using IMediaFinder.listMediaFiles from a jar requires overriding getInternalMediaNames");

    /// <summary>Find a specific asset by name, looking in each candidate folder of <see cref="ListMediaFolders"/>.</summary>
    IUncivFileHandle? FindMedia(string name) => ListMediaFolders()
        .SelectMany(folder => SupportedMediaExtensions.Select(ext => folder.Child(name + ext)))
        .FirstOrDefault(it => it.Exists);

    /// <summary>Enumerate all candidate media folders: current Ruleset mods, permanent audiovisual mods, then the builtin folder.</summary>
    IEnumerable<IUncivFileHandle> ListMediaFolders() => MediaFinderDefaults.ListMediaFolders(this);

    /// <summary>Enumerate all media files. Existence is ensured.</summary>
    IEnumerable<IUncivFileHandle> ListMediaFiles() => ListMediaFolders().SelectMany(folder =>
        folder.IsInternal && Environment.IsRunFromJar
            ? GetInternalMediaNames(folder)
                .SelectMany(name => SupportedMediaExtensions.Select(ext => folder.Child(name + ext)))
                .Where(it => it.Exists)
            : folder.List());

    IUncivFileHandle GetModMediaFolder(string modName) => Environment.GetModFolder(modName).Child(MediaSubFolderName);
}

public static class MediaFinderDefaults
{
    public static readonly IReadOnlySet<string> AudioExtensions = new HashSet<string> { ".mp3", ".ogg", ".wav" }; // Per Gdx docs, no aac/m4a
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg" };

    public static IEnumerable<IUncivFileHandle> ListMediaFolders(IMediaFinder finder) =>
        ListModFolders(finder)
            .Concat([finder.Environment.GetInternal(finder.MediaSubFolderName)])
            .Where(it => DirectoryExists(finder.Environment, it));

    private static bool DirectoryExists(IMediaEnvironment env, IUncivFileHandle folder) => !folder.IsInternal
        ? folder.Exists && folder.IsDirectory
        : env.IsAndroid
            ? folder.IsDirectory // An empty folder is no folder in this case
            : env.IsRunFromJar
                ? folder.Exists
                : folder.Exists && folder.IsDirectory;

    private static IEnumerable<IUncivFileHandle> ListModFolders(IMediaFinder finder)
    {
        // Order determines winner if several sources contain the same asset!
        // Mods chosen in the running game go first, including the BaseRuleset, which always is first in Ruleset.mods
        var ruleset = finder.GetRuleset();
        if (ruleset != null)
            foreach (var mod in ruleset.Mods)
                yield return finder.GetModMediaFolder(mod);
        // Permanent audiovisual mods next
        if (finder.Environment.IsGameInitialized)
            foreach (var mod in finder.Environment.VisualMods)
                yield return finder.GetModMediaFolder(mod);
    }
}

public class Sounds(IMediaEnvironment environment) : IMediaFinder
{
    public IMediaEnvironment Environment => environment;
    public string MediaSubFolderName => "sounds";
    public IReadOnlySet<string> SupportedMediaExtensions => MediaFinderDefaults.AudioExtensions;

    private List<string>? uncivSoundNamesCache;
    private List<string>? unitAttackSoundsCache;

    public IEnumerable<string> GetInternalMediaNames(IUncivFileHandle folder) =>
        (uncivSoundNamesCache ??= UncivSoundNames().ToList()).Concat(unitAttackSoundsCache ??= UnitAttackSounds().Select(it => it.Sound).ToList());

    // Reflection to enumerate a non-enum.
    protected static IEnumerable<string> UncivSoundNames() => typeof(UncivSound)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(it => it.PropertyType == typeof(UncivSound))
        .OrderBy(it => it.Name, StringComparer.Ordinal)
        .Select(it => ((UncivSound)it.GetValue(null)!).FileName);

    // Extract Unit attack sounds from the larger vanilla ruleset
    // Remember this replaces enumeration over *bundled* assets - not necessary for mods
    // Keeps unit around not for this class but for the labeled version
    protected static IEnumerable<(BaseUnit Unit, string Sound)> UnitAttackSounds()
    {
        var ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName());
        if (ruleset == null) return [];
        return ruleset.Units.Values
            .Where(it => it.AttackSound != null)
            .DistinctBy(it => it.AttackSound)
            .Select(it => (it, it.AttackSound!));
    }
}

public class Music(IMediaEnvironment environment) : IMediaFinder
{
    public IMediaEnvironment Environment => environment;
    public string MediaSubFolderName => "music";
    public IReadOnlySet<string> SupportedMediaExtensions => MediaFinderDefaults.AudioExtensions;
    public IReadOnlyList<string> Names { get; } = ["Thatched Villagers - Ambient"];
    public IEnumerable<string> GetInternalMediaNames(IUncivFileHandle folder) => Names;
}

public class Voices(IMediaEnvironment environment) : IMediaFinder
{
    public IMediaEnvironment Environment => environment;
    public string MediaSubFolderName => "voices";
    public IReadOnlySet<string> SupportedMediaExtensions => MediaFinderDefaults.AudioExtensions;
}

public class Images(IMediaEnvironment environment) : IMediaFinder
{
    public IMediaEnvironment Environment => environment;
    public string MediaSubFolderName => "ExtraImages";
    public IReadOnlySet<string> SupportedMediaExtensions => MediaFinderDefaults.ImageExtensions;
    // no GetInternalMediaNames - no ListMediaFiles() for internal assets needed
}

/// <summary>
/// Specialized subclass to provide all accessible sounds with a human-readable label.
/// API: use <see cref="GetLabeledSounds"/> only. It returns entries where key is the UncivSound filename and value is the display label.
/// </summary>
public class LabeledSounds(IMediaEnvironment environment) : Sounds(environment), IMediaFinder
{
    // Also determines display order
    private static readonly (string Key, string Label)[] PrettifyUncivSoundNames =
    [
        ("", "None"),
        ("notification1", "Notification [1]"),
        ("notification2", "Notification [2]"),
        ("coin", "Buy"),
        ("construction", "Create"),
        ("paper", "Pick a tech"),
        ("policy", "Adopt policy"),
        ("setup", "Set up"),
        ("swap", "Swap units"),
    ];

    // Previous code in createNotificationSoundOptions also excluded UncivSound.Whoosh - now included
    private static readonly string[] Exclusions = ["nuke", "fire", "slider"];

    private readonly LinkedHashMap<string, string> cache = new();

    public IEnumerable<KeyValuePair<string, string>> GetLabeledSounds()
    {
        FillCache();
        return cache;
    }

    IEnumerable<IUncivFileHandle> IMediaFinder.ListMediaFolders() =>
        // JUSTIFY: T118 LabeledSounds deliberately lacks this API in Kotlin (permanent)
        throw new NotSupportedException("LabeledSounds does not support the normal IMediaFinder API");

    private static string Titlecase(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private void FillCache()
    {
        if (cache.Count != 0) return;
        CacheBuiltins();
        CacheMods();
        foreach (var sound in Exclusions) cache.Remove(sound);
    }

    private void CacheBuiltins()
    {
        foreach (var (key, label) in PrettifyUncivSoundNames) cache.Put(key, label);
        foreach (var sound in UncivSoundNames())
            if (!cache.ContainsKey(sound))
                cache.Put(sound, Titlecase(sound));
        foreach (var (unit, sound) in UnitAttackSounds())
            cache.Put(sound, $"[{unit.Name}] Attack Sound");
    }

    private void CacheMods()
    {
        if (!Environment.IsGameInitialized) return;
        foreach (var folder in MediaFinderDefaults.ListMediaFolders(this))
        {
            if (folder.IsInternal) continue;
            var mod = folder.Parent.Name;
            var ruleset = RulesetCache.Get(mod);
            if (ruleset != null)
            {
                foreach (var unit in ruleset.Units.Values)
                {
                    var sound = unit.AttackSound;
                    if (sound == null) continue;
                    if (cache.ContainsKey(sound)) continue;
                    cache.Put(sound, $"{Take(mod, 32)}: {{[{unit.Name}] Attack Sound}}");
                }
            }
            foreach (var file in folder.List())
            {
                var sound = file.NameWithoutExtension;
                if (cache.ContainsKey(sound)) continue;
                cache.Put(sound, $"{Take(mod, 32)}: {Titlecase(sound)} {{}}");
            }
        }
    }

    private static string Take(string s, int n) => s.Length <= n ? s : s[..n];
}
