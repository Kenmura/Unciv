using Godot;
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
    private static string? folder;

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
        Active = active;
    }

    private static FontFile? Load(FontChainEntry entry)
    {
        if (Files.TryGetValue(entry.File, out var cached)) return cached;
        folder ??= FindFolder();
        var data = folder == null ? null : ReadAll(System.IO.Path.Combine(folder, entry.File));
        if (data == null)
        {
            if (!MissingFiles.Contains(entry.File)) MissingFiles.Add(entry.File);
            GD.PushWarning($"Font {entry.File} ({entry.Family}) not found; {string.Join(", ", entry.Scripts)} text may show missing-glyph boxes");
            return null;
        }
        return Files[entry.File] = new FontFile { Data = data, FontName = entry.Family, Antialiasing = TextServer.FontAntialiasing.Gray };
    }

    private static byte[]? ReadAll(string path)
    {
        try { return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null; }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? FindFolder()
    {
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
