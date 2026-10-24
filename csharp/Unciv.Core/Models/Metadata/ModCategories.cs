namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/ModCategories.kt
//
// Only the Category value class is ported. The ModCategories list (loaded from jsons/ModCategories.json, merged from
// GitHub topics, shown in a UI select box) is client and network code: Won't port.
public sealed class ModCategory(string label, string topic, bool hidden, string createDate, string modifyDate)
{
    public static ModCategory All { get; } = new("All mods", "unciv-mod", false, "", "");

    public ModCategory() : this("", "", false, "", "")
    {
    }

    public string Label { get; } = label;

    public string Topic { get; } = topic;

    public bool Hidden { get; } = hidden;

    /// <summary>Copy of github created_at, no function except help evaluate.</summary>
    public string CreateDate { get; } = createDate;

    /// <summary>Copy of github updated_at, no function except help evaluate.</summary>
    public string ModifyDate { get; set; } = modifyDate;

    // Kotlin: equality and hash look at the topic only
    public override bool Equals(object? obj) => ReferenceEquals(this, obj) || (obj is ModCategory other && Topic == other.Topic);

    public override int GetHashCode() => Unciv.Utils.JavaHash.HashCode(Topic);

    public override string ToString() => Label;
}
