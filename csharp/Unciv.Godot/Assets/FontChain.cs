using Godot;
using Unciv.Assets;
using Unciv.Assets.Localization;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C025: builds the procedural <c>font/default</c>, the engine's built-in font with the OFL fallback files of <see cref="FontChainSpec"/> behind it.
/// Only the files a language needs are loaded (see <see cref="Configure"/>). The files are found in the <c>fonts</c> folder of the client's Assets (<c>res://Assets/fonts</c>, or the same folder of the <c>Unciv.Godot</c> project when the
/// running project is the test harness). A file that is missing or unreadable is skipped with a warning (and listed in <see cref="Missing"/>).
/// </summary>
public static class FontChain
{
    private static readonly FontVariation Chain = new() { BaseFont = ThemeDB.FallbackFont };
    private static readonly Dictionary<string, FontFile> Files = [];
    private static readonly List<string> MissingFiles = [];
    private static readonly Dictionary<string, FontVariation> Styles = [];
    private static Godot.Collections.Array<Font> CurrentFallbacks = [];
    private static string? FontFolder;

    /// <summary>C062: the variable Noto faces behind the type scale. Sans is also in <see cref="FontChainSpec"/> (its Latin fallback); serif is only for screen titles and banners.</summary>
    private const string SansFile = "NotoSans.ttf";
    private const string SerifFile = "NotoSerif.ttf";

    /// <summary>Files of <see cref="FontChainSpec"/> that were wanted but could not be loaded.</summary>
    public static IReadOnlyList<string> Missing => MissingFiles;

    /// <summary>The files behind the built-in font right now, in order.</summary>
    public static IReadOnlyList<string> Active { get; private set; } = [];

    /// <summary>The font every theme uses. One instance for the life of the program: <see cref="Configure"/> changes what is behind it and live controls follow.</summary>
    public static Font Default => Chain;

    /// <summary>
    /// Puts behind the built-in font exactly the files needed to draw <paramref name="codePoints"/>: one per script they use, and the Latin/Cyrillic/Greek one only when the built-in font
    /// lacks one of those letters. Fallback fonts make every line taller (a font's height is the largest of its chain), so a language that needs none keeps the built-in font alone and
    /// its exact layout, and the 2 MB CJK file is read only when a CJK language is chosen.
    /// </summary>
    public static void Configure(IEnumerable<int> codePoints)
    {
        var needed = codePoints.Distinct().ToList();
        var scripts = needed.Select(ScriptClassifier.Of).Where(s => s != TextScript.Common).ToHashSet();
        var fallbacks = new Godot.Collections.Array<Font>();
        var active = new List<string>();
        foreach (var entry in FontChainSpec.Entries)
        {
            bool alphabetic = entry.Scripts.Contains(TextScript.Latin);
            bool wanted = alphabetic
                ? needed.Any(cp => entry.Scripts.Contains(ScriptClassifier.Of(cp)) && !ThemeDB.FallbackFont.HasChar(cp))
                : entry.Scripts.Any(scripts.Contains);
            if (!wanted) continue;
            var file = Load(entry);
            if (file == null) continue;
            fallbacks.Add(file);
            active.Add(entry.File);
        }
        Chain.Fallbacks = fallbacks;
        CurrentFallbacks = fallbacks;
        Active = active;
        foreach (var style in Styles.Values) style.Fallbacks = StyleFallbacks();
    }

    /// <summary>
    /// C062: the font of a step of the type scale: the variable Noto Sans or Noto Serif face at the step's weight (OpenType <c>wght</c>), tabular figures when the style asks,
    /// with the script fallbacks of <see cref="Default"/> behind it so a non-Latin title still draws. One instance per step for the life of the program. When the Noto file is missing
    /// (listed in <see cref="Missing"/>) the built-in font stands in.
    /// </summary>
    public static Font For(TypeStyle style)
    {
        if (Styles.TryGetValue(style.Name, out var made)) return made;
        var server = TextServerManager.GetPrimaryInterface();
        var face = LoadFile(style.Serif ? SerifFile : SansFile, style.Serif ? "Noto Serif" : "Noto Sans", "Latin");
        var variation = new FontVariation
        {
            BaseFont = face ?? ThemeDB.FallbackFont,
            VariationOpentype = new Godot.Collections.Dictionary { [server.NameToTag("wght")] = style.Weight },
            SpacingGlyph = style.Tracking, // not UI-scaled: 1 px at every scale
            Fallbacks = StyleFallbacks(),
        };
        if (style.Tabular) variation.OpentypeFeatures = new Godot.Collections.Dictionary { [server.NameToTag("tnum")] = 1 };
        return Styles[style.Name] = variation;
    }

    private static Godot.Collections.Array<Font> StyleFallbacks()
    {
        var all = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        foreach (var font in CurrentFallbacks) all.Add(font);
        return all;
    }

    private static FontFile? Load(FontChainEntry entry) => LoadFile(entry.File, entry.Family, string.Join(", ", entry.Scripts));

    private static FontFile? LoadFile(string file, string family, string needFor)
    {
        if (Files.TryGetValue(file, out var cached)) return cached;
        FontFolder ??= FindFolder();
        var data = FontFolder == null ? null : ReadAll(System.IO.Path.Combine(FontFolder, file));
        if (data == null)
        {
            if (!MissingFiles.Contains(file)) MissingFiles.Add(file);
            GD.PushWarning($"Font {file} ({family}) not found; {needFor} text may show missing-glyph boxes");
            return null;
        }
        return Files[file] = new FontFile { Data = data, FontName = family, Antialiasing = TextServer.FontAntialiasing.Gray };
    }

    private static byte[]? ReadAll(string path)
    {
        try { return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? FindFolder()
    {
        if (ContentRoot.Directory is { } content && System.IO.File.Exists(System.IO.Path.Combine(content, "Assets", "fonts", "OFL.txt"))) return System.IO.Path.Combine(content, "Assets", "fonts"); // T276
        for (string d = ProjectSettings.GlobalizePath("res://"); !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d.TrimEnd('/')) ?? "")
        {
            foreach (string candidate in new[] { System.IO.Path.Combine(d, "Assets", "fonts"), System.IO.Path.Combine(d, "Unciv.Godot", "Assets", "fonts") })
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "OFL.txt"))) return candidate;
            }
        }
        return null;
    }
}
