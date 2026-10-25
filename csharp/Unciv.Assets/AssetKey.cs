using System.Text;

namespace Unciv.Assets;

/// <summary>
/// Typed asset name such as <c>terrain/Grassland</c> or <c>icon/stat/Gold</c>. The category is everything before the last
/// slash, the name everything after. Keys are derived from ruleset names, so the name may contain spaces or mod-supplied
/// characters; <see cref="FileStem"/> is the filesystem-safe, collision-free form used for override files.
/// </summary>
public readonly record struct AssetKey
{
    public const string DefaultName = "_default";

    public string Category { get; }
    public string Name { get; }

    public AssetKey(string category, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (category.Contains("//", StringComparison.Ordinal) || category.StartsWith('/') || category.EndsWith('/'))
            throw new ArgumentException($"Malformed asset category '{category}'", nameof(category));
        if (name.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException($"Asset name '{name}' must not contain '/'", nameof(name));
        Category = category;
        Name = name;
    }

    /// <summary>Key for a ruleset-supplied name: a '/' in the name (mods may use any text) becomes '_' so it cannot be mistaken for a category separator.</summary>
    public static AssetKey Of(string category, string name) => new(category, name.Replace('/', '_'));

    /// <summary>Splits at the last slash. Throws <see cref="FormatException"/> when there is no category or name.</summary>
    public static AssetKey Parse(string key)
    {
        if (!TryParse(key, out var result)) throw new FormatException($"Not a valid asset key: '{key}'");
        return result;
    }

    public static bool TryParse(string? key, out AssetKey result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var slash = key.LastIndexOf('/');
        if (slash <= 0 || slash == key.Length - 1) return false;
        var category = key[..slash];
        if (category.Contains("//", StringComparison.Ordinal) || category.StartsWith('/') || category.EndsWith('/')) return false;
        result = new AssetKey(category, key[(slash + 1)..]);
        return true;
    }

    public bool IsDefault => Name == DefaultName;

    /// <summary>The <c>category/_default</c> key every chain ends in.</summary>
    public AssetKey Default => new(Category, DefaultName);

    /// <summary>Filesystem-safe relative path stem (no extension): see Sanitize.</summary>
    public string FileStem
    {
        get
        {
            var sb = new StringBuilder(Category.Length + Name.Length + 1);
            foreach (var segment in Category.Split('/'))
            {
                sb.Append(Sanitize(segment)).Append('/');
            }
            sb.Append(Sanitize(Name));
            return sb.ToString();
        }
    }

    /// <summary>Percent-encodes the UTF-8 bytes of everything outside [A-Za-z0-9_-.] so distinct names never share a file; a segment of only dots is fully encoded so it cannot mean '.' or '..'.</summary>
    private static string Sanitize(string part)
    {
        var allDots = part.All(c => c == '.');
        var sb = new StringBuilder(part.Length);
        foreach (var c in part)
        {
            if (!allDots && c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.') sb.Append(c);
            else foreach (var b in Encoding.UTF8.GetBytes(c.ToString())) sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    public override string ToString() => Category + "/" + Name;
}
