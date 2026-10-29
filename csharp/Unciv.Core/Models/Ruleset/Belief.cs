using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Belief.kt
public class Belief : RulesetObject, ICivilopediaText
{
    public Belief()
    {
    }

    public Belief(BeliefType type)
    {
        Type = type;
    }

    public BeliefType Type { get; set; } = BeliefType.None;

    public override UniqueTarget GetUniqueTarget() =>
        Type.IsFounder() ? UniqueTarget.FounderBelief : UniqueTarget.FollowerBelief;

    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        if (multiFilter)
        {
            return MultiFilter.Filter(filter, it =>
                MatchesSingleFilter(it, state) ||
                (state != null && self.HasTagUnique(it, state)) ||
                (state == null && self.HasTagUnique(it)));
        }
        return MatchesSingleFilter(filter, state) ||
            (state != null && self.HasTagUnique(filter, state)) ||
            (state == null && self.HasTagUnique(filter));
    }

    public bool MatchesSingleFilter(string filter, GameContext? state = null)
    {
        _ = state;
        if (filter == Name) return true;
        if (Constants.All.Contains(filter)) return true;
        return filter == Type.ToString(); // Belief type
    }

    public override string MakeLink() => $"Belief/{Name}";

    public FormattedLine? GetCivilopediaTextHeader() =>
        new(Name, icon: MakeLink(), header: 2, color: Type == BeliefType.None ? "#e34a2b" : "");

    public int GetSortGroup(Ruleset ruleset) => (int)Type;

    public string? GetSubCategory(Ruleset ruleset) => Type.GetSubCategory();

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => GetCivilopediaTextLines(context, false);

    /// <summary>This special overload is called from Religion overview and Religion picker</summary>
    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context, bool withHeader) =>
        FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            if (withHeader)
            {
                b.Add(Name, size: Constants.HeadingFontSize, centered: true, link: MakeLink());
                b.Space();
            }
            if (Type != BeliefType.None)
                b.Add($"{{Type}}: {{{Type}}}", color: Type.GetColor(), centered: withHeader);
            b.AddUniques(this, SeparatorType.None);
        });

    // Kotlin reads UncivGame.getGameInfoOrNull() implicitly; here the caller passes the loaded game, if any (gameInfo).
    public static IEnumerable<Belief> GetBeliefsMatching(string name, Ruleset ruleset, IGameContextGameInfo? gameInfo = null) =>
        ruleset.Beliefs.Values
            .Where(it => !((IHasUniques)it).IsHiddenFromCivilopedia(gameInfo, ruleset))
            .Where(it => ((IHasUniques)it).HasUniquesMentioning(name));

    /// <summary>
    /// Get CivilopediaText lines for all Beliefs referencing a given name in an unique parameter,
    /// With optional spacing and "See Also:" header.
    /// </summary>
    public static IEnumerable<FormattedLine> GetCivilopediaTextMatching(string name, Ruleset ruleset, bool withSeeAlso = true, IGameContextGameInfo? gameInfo = null)
    {
        var matchingBeliefs = GetBeliefsMatching(name, ruleset, gameInfo).ToList();
        if (matchingBeliefs.Count == 0) yield break;
        if (withSeeAlso)
        {
            yield return new FormattedLine();
            yield return new FormattedLine("{See also}:");
        }
        foreach (var belief in matchingBeliefs)
            yield return new FormattedLine(belief.Name, link: belief.MakeLink(), indent: 1);
    }

    public static Belief GetCivilopediaBeliefsEntry(CivilopediaContext context) => new()
    {
        Name = "Beliefs",
        CivilopediaText = FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            b.Add("There are four types of beliefs: Pantheon, Founder, Follower and Enhancer beliefs.");
            b.Add("Pantheon and Follower beliefs apply to each city following your religion, while Founder and Enhancer beliefs only apply to the founder of a religion.");
        }),
    };

    public static Belief GetCivilopediaReligionEntry(CivilopediaContext context) => new()
    {
        Name = "Religions",
        CivilopediaText = FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            foreach (var religion in context.Ruleset.Religions.OrderBy(it => it.Tr(hideIcons: true), context.GetCollator()))
                b.Add(religion, icon: $"Belief/{religion}");
        }),
    };
}

/// <summary>
/// Subtypes of Beliefs - directly deserialized.
/// Kotlin <c>isFollower</c> (Uniques processed per city) and <c>isFounder</c> (Uniques processed globally for founding civ only)
/// are in <see cref="BeliefTypeExtensions"/>.
/// </summary>
public enum BeliefType
{
    None,
    Pantheon,
    Founder,
    Follower,
    Enhancer,
    Any,
    /// <summary>Brave New World (B004): the fifth, Reformation-tier belief. Appended after <c>Any</c> so the Kotlin ordinals stay; it acts like a founder belief (global, for the founding civ).</summary>
    Reformation,
}

public static class BeliefTypeExtensions
{
    public static string GetColor(this BeliefType type) => type switch
    {
        BeliefType.Pantheon => "#44c6cc",
        BeliefType.Founder => "#c00000",
        BeliefType.Follower => "#ccaa44",
        BeliefType.Enhancer => "#72cc45",
        BeliefType.Reformation => "#cc72cc",
        _ => "",
    };

    public static bool IsFollower(this BeliefType type) => type is BeliefType.Pantheon or BeliefType.Follower;

    public static bool IsFounder(this BeliefType type) => type is BeliefType.Founder or BeliefType.Enhancer or BeliefType.Reformation;

    public static string? GetSubCategory(this BeliefType type) => type is BeliefType.None or BeliefType.Any ? null : type.ToString();
}
