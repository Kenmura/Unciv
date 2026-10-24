using Unciv.Models;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/DiplomacyManager.kt (enum RelationshipLevel)

public enum RelationshipLevel
{
    // DiplomaticStatus.War is tested separately for the Diplomacy Screen. Colored RED.
    // DiplomaticStatus.DefensivePact - similar. Colored PURPLE.
    Unforgivable,
    Enemy,
    Afraid,
    Competitor,
    Neutral,
    Favorable,
    Friend,
    Ally,
}

public static class RelationshipLevelExtensions
{
    private static readonly EngineColor[] Colors =
    [
        Rgba(0x8b4513ff), // Unforgivable: Color.BROWN
        Rgba(0xffa500ff), // Enemy: Color.ORANGE
        Rgba(0xffff00ff), // Afraid: Color.YELLOW
        Rgba(0x7f7f7fff), // Competitor: Color.GRAY
        Rgba(0xffffffff), // Neutral: Color.WHITE
        Rgba(0x14cc3cff), // Favorable: HSV(133,90,80)
        Rgba(0x4169e1ff), // Friend: Color.ROYAL
        Rgba(0x00ffffff), // Ally: Color.CYAN
    ];

    private static EngineColor Rgba(uint rgba8888) => new(
        ((rgba8888 & 0xff000000) >> 24) / 255f, ((rgba8888 & 0x00ff0000) >> 16) / 255f,
        ((rgba8888 & 0x0000ff00) >> 8) / 255f, (rgba8888 & 0x000000ff) / 255f);

    /// <summary>Kotlin <c>RelationshipLevel.color</c> (a fresh copy, the colors are mutable).</summary>
    public static EngineColor Color(this RelationshipLevel level) => Colors[(int)level].Cpy();

    /// <summary>Kotlin <c>operator plus</c>: moves by <paramref name="delta"/> levels, clamped to the first and last level.</summary>
    public static RelationshipLevel Plus(this RelationshipLevel level, int delta)
    {
        int newOrdinal = Math.Clamp((int)level + delta, 0, Enum.GetValues<RelationshipLevel>().Length - 1);
        return (RelationshipLevel)newOrdinal;
    }
}
