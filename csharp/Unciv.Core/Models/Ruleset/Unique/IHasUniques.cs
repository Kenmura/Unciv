using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// The parts of <c>Ruleset</c> that <see cref="IHasUniques.IsHiddenFromCivilopedia"/> guesses from when no game is loaded.
/// T034's <c>Ruleset</c> implements this.
/// </summary>
public interface IRulesetFeatureView
{
    /// <summary><c>ruleset.beliefs.isNotEmpty()</c></summary>
    bool HasBeliefs { get; }
    /// <summary><c>ruleset.nations.values.any { it.spyNames.isNotEmpty() }</c></summary>
    bool HasSpyNames { get; }
}

/// <summary>
/// The parts of <c>Ruleset</c> that <see cref="IHasUniques.RequiredTechnologies"/>, <see cref="IHasUniques.Era"/>, <see cref="IHasUniques.TechColumn"/>
/// and <see cref="IHasUniques.AvailableInEra"/> read. T034's <c>Ruleset</c> implements this.
/// </summary>
public interface IRulesetTechView
{
    /// <summary><c>ruleset.technologies[name]</c></summary>
    Tech.Technology? GetTechnology(string name);
    /// <summary><c>ruleset.eras[name]</c></summary>
    Tech.Era? GetEra(string? name);
}

/// <summary>
/// Common interface for all 'ruleset objects' that have Uniques, like BaseUnit, Nation, etc.
/// Ported from core/src/com/unciv/models/ruleset/unique/IHasUniques.kt.
/// </summary>
/// <remarks>
/// <c>requiredTechnologies</c>, <c>era</c>, <c>techColumn</c> and <c>availableInEra</c> take an <see cref="IRulesetTechView"/>
/// instead of <c>Ruleset</c>.
/// The <c>isHiddenFromCivilopedia(ruleset)</c> overload reads <c>UncivGame.getGameInfoOrNull()</c>, a client global: callers pass the
/// <c>gameInfo</c> explicitly to the other overload.
/// </remarks>
public interface IHasUniques : INamed, IRulesetObjectParameterView
{
    // The Kotlin `uniques: ArrayList<String>`. Can not be a hashset as that would remove doubles.
    // Declared `new` because IRulesetObjectParameterView only needs a read-only view of the same list.
    new List<string> Uniques { get; set; }

    new string Name { get; set; }

    // Every implementation should cache these (Kotlin: `by lazy (::thingsProvider)`, transient)
    IReadOnlyList<Unique> UniqueObjects { get; }
    UniqueMap UniqueMap { get; }

    IReadOnlyCollection<string> IRulesetObjectParameterView.Uniques => Uniques;
    bool IRulesetObjectParameterView.HasTagUnique(string uniqueTag) => UniqueMap.HasTagUnique(uniqueTag);
    bool IRulesetObjectParameterView.HasTagUniqueIgnoringConditionals(string uniqueTag) =>
        UniqueMap.HasMatchingTagUnique(uniqueTag, GameContext.IgnoreConditionalsState);

    IReadOnlyList<Unique> UniqueObjectsProvider() => UniqueObjectsProvider(Uniques);
    UniqueMap UniqueMapProvider() => UniqueMapProvider(UniqueObjects);

    IReadOnlyList<Unique> UniqueObjectsProvider(IReadOnlyList<string> uniques)
    {
        if (uniques.Count == 0) return Array.Empty<Unique>();
        return uniques.Select(it => new Unique(it, GetUniqueTarget(), Name)).ToList();
    }

    UniqueMap UniqueMapProvider(IReadOnlyList<Unique> uniqueObjects)
    {
        var newUniqueMap = new UniqueMap();
        if (uniqueObjects.Count != 0)
            newUniqueMap.AddUniques(uniqueObjects);
        return newUniqueMap;
    }

    /// <summary>
    /// Technically not currently needed, since the unique target can be retrieved from every unique in the uniqueObjects,
    /// But making this a function is relevant for future "unify Unciv object" plans ;)
    /// </summary>
    UniqueTarget GetUniqueTarget();

    /// <summary><see cref="ForEachMatchingUnique(UniqueType, GameContext, Action{Unique})"/> faster, for cases that require high perf</summary>
    IEnumerable<Unique> GetMatchingUniques(UniqueType uniqueType, GameContext? state = null) =>
        UniqueMap.GetMatchingUniques(uniqueType, state ?? GameContext.EmptyState);

    void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Func<Unique, bool> filter, Action<Unique> op) =>
        UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, filter, op);

    void ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<Unique> op) =>
        UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);

    IEnumerable<Unique> GetMatchingTagUniques(string uniqueTag, GameContext? state = null) =>
        UniqueMap.GetMatchingTagUniques(uniqueTag, state ?? GameContext.EmptyState);

    bool HasUnique(UniqueType uniqueType, GameContext? state = null) =>
        UniqueMap.HasMatchingUnique(uniqueType, state ?? GameContext.EmptyState);

    bool HasTagUnique(string uniqueTag, GameContext? state) =>
        UniqueMap.HasMatchingTagUnique(uniqueTag, state ?? GameContext.EmptyState);

    new bool HasTagUnique(string uniqueTag) =>
        UniqueMap.HasTagUnique(uniqueTag);

    /// <summary>
    /// Helper used for Civilopedia when finding cross-references, e.g. for see-also lists
    /// </summary>
    /// <returns><c>true</c> if any unique parameter is equal to <paramref name="name"/></returns>
    bool HasUniquesMentioning(string name) =>
        UniqueObjects.Any(unique => unique.Params.Any(it => it == name));

    IEnumerable<string> TechsRequiredByUniques()
    {
        var availabilityUniques = GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
            .Concat(GetMatchingUniques(UniqueType.CanOnlyBeBuiltWhen, GameContext.IgnoreConditionalsState));

        return availabilityUniques
            // Currently an OnlyAvailableWhen can have multiple conditionals, implicitly a conjunction.
            // Therefore, if any of its several conditionals is a ConditionalTech, then that tech is required.
            .SelectMany(it => it.Modifiers)
            .Where(it => it.Type == UniqueType.ConditionalTech)
            .Select(it => it.Params[0]);
    }

    IEnumerable<string> LegacyRequiredTechs() => Enumerable.Empty<string>();

    IEnumerable<string> RequiredTechs() => LegacyRequiredTechs().Concat(TechsRequiredByUniques());

    IEnumerable<Tech.Technology?> RequiredTechnologies(IRulesetTechView ruleset) =>
        RequiredTechs().Select(ruleset.GetTechnology);

    /// <summary>This will return null only if requiredTechnologies() is empty or all required techs have no eraNumber</summary>
    Tech.Era? Era(IRulesetTechView ruleset)
    {
        // Kotlin maxByOrNull: the first element with the maximum key wins
        Tech.Era? best = null;
        var bestKey = 0;
        var first = true;
        foreach (var tech in RequiredTechnologies(ruleset))
        {
            var era = ruleset.GetEra(tech?.Era());
            var key = era?.EraNumber ?? 0;
            if (first || key > bestKey) { best = era; bestKey = key; first = false; }
        }
        return best;
    }

    /// <summary>This will return null only if *all* required techs have null TechColumn.</summary>
    Tech.TechColumn? TechColumn(IRulesetTechView ruleset)
    {
        Tech.TechColumn? best = null;
        foreach (var column in RequiredTechnologies(ruleset).Select(it => it?.Column).OfType<Tech.TechColumn>())
            if (best == null || column.ColumnNumber > best.ColumnNumber) best = column;
        return best;
    }

    bool AvailableInEra(IRulesetTechView ruleset, string requestedEra)
    {
        var eraAvailable = Era(ruleset);
        if (eraAvailable == null) return true; // No technologies are required, so available in the starting era.
        return eraAvailable.EraNumber <= ruleset.GetEra(requestedEra).NotNull().EraNumber;
    }

    /// <summary>
    /// Product of the <see cref="UniqueType.AiChoiceWeight"/> uniques; for a <see cref="Unit.BaseUnit"/> or <see cref="Building"/> also
    /// of the civ's personality <see cref="UniqueType.PersonalityAiWeight"/> uniques whose filter matches.
    /// </summary>
    float GetWeightForAiDecision(GameContext gameContext)
    {
        var weight = 1f;
        foreach (var unique in GetMatchingUniques(UniqueType.AiChoiceWeight, gameContext))
            weight *= unique.Params[0].ToPercent();

        // todo (upstream): we should have an IHasMatchesFilter?
        if (this is not (Unit.BaseUnit or Building)) return weight;
        var personality = gameContext.CivInfo?.GetPersonality();
        if (personality == null) return weight;

        foreach (var unique in ((IHasUniques)personality).GetMatchingUniques(UniqueType.PersonalityAiWeight, gameContext))
        {
            var factor = unique.Params[0].ToPercent();
            switch (this)
            {
                case Unit.BaseUnit unit when unit.MatchesFilter(unique.Params[1], gameContext):
                    weight *= factor;
                    break;
                case Building building when building.MatchesFilter(unique.Params[1], gameContext):
                    weight *= factor;
                    break;
            }
        }
        return weight;
    }

    /// <summary>Do this object's <see cref="UniqueType.OnlyAvailable"/> and <see cref="UniqueType.Unavailable"/> uniques all pass for <paramref name="gameContext"/>?</summary>
    bool IsAvailable(GameContext gameContext) =>
        !GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Any(it => !it.ConditionalsApply(gameContext)) &&
        !GetMatchingUniques(UniqueType.Unavailable, gameContext).Any();

    /// <summary>
    /// Is this ruleset object unavailable as determined by settings chosen at game start?
    ///
    /// - <b>Not</b> checked: HiddenFromCivilopedia - That is a Mod choice and less a user choice and these objects should otherwise work.
    /// - Default implementation checks disabling by Religion, Espionage or Victory types.
    /// - Overrides need to deal with e.g. Era-specific wonder disabling, no-nukes, ruin rewards by difficulty, and so on!
    /// </summary>
    bool IsUnavailableBySettings(IGameContextGameInfo gameInfo) => IsUnavailableBySettingsDefault(this, gameInfo);

    /// <summary>The default implementation of <see cref="IsUnavailableBySettings"/>; C# classes cannot call an overridden default interface method as `super`, so overrides call this.</summary>
    static bool IsUnavailableBySettingsDefault(IHasUniques self, IGameContextGameInfo gameInfo)
    {
        var gameContext = new GameContext(gameInfo: gameInfo);

        if (self.GetMatchingUniques(UniqueType.Unavailable, GameContext.IgnoreConditionalsState)
            .Any(unique => unique.Modifiers.Any(it =>
                it.Type != null && GameBasedConditionals.Contains(it.Type.Value)
                && Conditionals.ConditionalApplies(null, it, gameContext))))
            return true;

        if (self.GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
            .Any(unique => unique.Modifiers.Any(it =>
                it.Type != null && GameBasedConditionals.Contains(it.Type.Value)
                && !Conditionals.ConditionalApplies(null, it, gameContext))))
            return true;

        return false;
    }

    /// <summary>
    /// Is this ruleset object hidden from Civilopedia?
    ///
    /// - Obviously, the <see cref="UniqueType.HiddenFromCivilopedia"/> test is done here (and nowhere else - exception TranslationFileWriter for Tutorials).
    /// - Includes the <see cref="IsUnavailableBySettings"/> test if <paramref name="gameInfo"/> is known, otherwise existence of Religion/Espionage is guessed from <paramref name="ruleset"/>,
    ///   and all victory types are assumed enabled.
    /// - Note: RulesetObject-type specific overrides should not be necessary.
    /// </summary>
    /// <param name="gameInfo">Civilopedia must also be able to run from MainMenu without a game loaded. In that case only this parameter can be <c>null</c>. So if you know it - provide!</param>
    /// <param name="ruleset">Required if <paramref name="gameInfo"/> is null, otherwise optional (but with both null this will simply return <c>false</c>).</param>
    bool IsHiddenFromCivilopedia(IGameContextGameInfo? gameInfo, IRulesetFeatureView? ruleset = null)
    {
        if (HasUnique(UniqueType.HiddenFromCivilopedia, new GameContext(gameInfo: gameInfo))) return true;
        if (gameInfo != null && IsUnavailableBySettings(gameInfo)) return true;
        if (gameInfo == null && ruleset != null)
        {
            /* No game is loaded, but we know the Ruleset. This happens when opening Civilopedia from MainMenuScreen right after launch.
             * We can assume: If the Ruleset has Religion/Espionage, the user would enable it for a hypothetical game, so we shouldn't hide anything.
             * But we can't assume how mods may use the possible combinations of positive/negative modifiers
             *     e.g. a mod switches between two versions of the same object depending on user choice
             *     better do a check based on the best guess for the availability of these features.
             */
            if (ShouldBeHiddenIfNoGameLoaded(
                ruleset.HasBeliefs,
                UniqueType.ConditionalReligionEnabled,
                UniqueType.ConditionalReligionDisabled))
                return true;
            if (ShouldBeHiddenIfNoGameLoaded(
                ruleset.HasSpyNames,
                UniqueType.ConditionalEspionageEnabled,
                UniqueType.ConditionalEspionageDisabled))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Common for Religion/Espionage: Hidden check when no game is loaded
    /// </summary>
    /// <param name="hasFeature">Best guess from the Ruleset whether the feature is available</param>
    /// <param name="enabler">The modifier testing feature is on: <c>ConditionalReligionEnabled</c> or <c>ConditionalEspionageEnabled</c></param>
    /// <param name="disabler">The modifier testing feature is off: <c>ConditionalReligionDisabled</c> or <c>ConditionalEspionageDisabled</c></param>
    private bool ShouldBeHiddenIfNoGameLoaded(bool hasFeature, UniqueType enabler, UniqueType disabler)
    {
        foreach (var unique in GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState))
        {
            if (unique.HasModifier(enabler)) return !hasFeature;
            if (unique.HasModifier(disabler)) return hasFeature;
        }
        foreach (var unique in GetMatchingUniques(UniqueType.Unavailable, GameContext.IgnoreConditionalsState))
        {
            if (unique.HasModifier(enabler)) return hasFeature;
            if (unique.HasModifier(disabler)) return !hasFeature;
        }
        return false;
    }

    static readonly IReadOnlySet<UniqueType> GameBasedConditionals = new HashSet<UniqueType>
    {
        UniqueType.ConditionalVictoryDisabled,
        UniqueType.ConditionalVictoryEnabled,
        UniqueType.ConditionalSpeed,
        UniqueType.ConditionalDifficulty,
        UniqueType.ConditionalDifficultyOrHigher,
        UniqueType.ConditionalDifficultyOrLower,
        UniqueType.ConditionalReligionEnabled,
        UniqueType.ConditionalReligionDisabled,
        UniqueType.ConditionalEspionageEnabled,
        UniqueType.ConditionalEspionageDisabled,
    };
}
