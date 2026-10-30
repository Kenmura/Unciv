using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.UI.Screens.MapEditorScreen;
using Unciv.Utils;

namespace Unciv.Queries.Setup;

// Name: Ruleset object name
public sealed record SetupNation(string Name, bool IsMajorCiv, bool IsCityState, bool IsSpectator, bool Choosable);

public sealed record SetupSpeed(string Name);

public sealed record SetupDifficulty(string Name);

public sealed record SetupVictory(string Name, bool Selectable);

public sealed record SetupEra(string Name, int EraNumber);

public sealed record SetupMod(string Name, bool IsBaseRuleset, bool IsExtensionMod);

// MapOptions: the value domains of the map options; all lists are in the order the client should show them
public sealed record SetupMapOptions(
    IReadOnlyList<string> Shapes,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Mirroring,
    IReadOnlyList<string> GeneratorSteps);

public sealed record NewGameOptions(
    string BaseRuleset,
    IReadOnlyList<string> Mods,
    IReadOnlyList<SetupMod> AvailableRulesets,
    IReadOnlyList<SetupNation> Nations,
    IReadOnlyList<SetupDifficulty> Difficulties,
    IReadOnlyList<SetupSpeed> Speeds,
    IReadOnlyList<SetupVictory> Victories,
    IReadOnlyList<SetupEra> Eras,
    SetupMapOptions MapOptions,
    GameParameters DefaultGameParameters,
    MapParameters DefaultMapParameters);

// T199: the data a new-game screen (or a host lobby) offers, for a base ruleset plus mods. Pre-game there is no GameInfo and no viewer,
// so unlike the T195 queries this does not take a GameView and is not registered by the QueryRegistry scan: it reads the RulesetCache.
// Lists are in ruleset file order (LinkedHashMap order, not alphabetical: sorting is the client's job). 'Random' nations and a random
// number of players are not resolved here, GameStarter does that with the seeded RNG. Pure: no RNG, no clock, no state.
public sealed record NewGameOptionsQuery(string? BaseRuleset = null, IReadOnlyList<string>? Mods = null)
{
    public const string Type = "setup.NewGameOptions";

    // Kotlin PlayerPickerTable.isChoosable
    public static bool IsChoosable(Models.Ruleset.Nation.Nation nation) =>
        (nation.IsMajorCiv || nation.IsSpectator) && !((IHasUniques)nation).HasUnique(UniqueType.WillNotBeChosenForNewGames);

    public NewGameOptions Run()
    {
        var mods = new LinkedHashSet<string>(Mods ?? []);
        var baseName = BaseRuleset ?? Models.Metadata.BaseRuleset.Civ_V_BNW.FullName();
        var ruleset = RulesetCache.GetComplexRuleset(mods, baseName);
        var effectiveBase = RulesetCache.Get(baseName)?.ModOptions.IsBaseRuleset == true
            ? baseName
            : RulesetCache.GetVanillaRuleset().Name;

        var available = RulesetCache.GetSortedBaseRulesets()
            .Select(it => new SetupMod(it, true, false))
            .Concat(RulesetCache.Values
                .Where(it => !it.ModOptions.IsBaseRuleset)
                .OrderBy(it => it.Name, StringComparer.Ordinal)
                .Select(it => new SetupMod(it.Name, false, ModCompatibility.IsExtensionMod(it))))
            .ToList();

        var defaults = new GameParameters { BaseRuleset = effectiveBase };
        foreach (var mod in mods.Where(RulesetCache.ContainsKey)) defaults.Mods.Add(mod);
        defaults.Difficulty = ruleset.Difficulties.ContainsKey(defaults.Difficulty) ? defaults.Difficulty : ruleset.Difficulties.Keys.FirstOrDefault() ?? defaults.Difficulty;
        defaults.Speed = ruleset.Speeds.ContainsKey(defaults.Speed) ? defaults.Speed : ruleset.Speeds.Keys.FirstOrDefault() ?? defaults.Speed;
        defaults.StartingEra = ruleset.Eras.ContainsKey(defaults.StartingEra) ? defaults.StartingEra : ruleset.Eras.Keys.FirstOrDefault() ?? defaults.StartingEra;
        defaults.VictoryTypes = ruleset.SelectableVictories().Select(it => it.Name).ToList();

        return new NewGameOptions(
            effectiveBase,
            defaults.Mods.ToList(),
            available,
            ruleset.Nations.Values
                .Where(it => !it.IsBarbarian)
                .Select(it => new SetupNation(it.Name, it.IsMajorCiv, it.IsCityState, it.IsSpectator, IsChoosable(it)))
                .ToList(),
            ruleset.Difficulties.Keys.Select(it => new SetupDifficulty(it)).ToList(),
            ruleset.Speeds.Keys.Select(it => new SetupSpeed(it)).ToList(),
            ruleset.Victories.Values.Select(it => new SetupVictory(it.Name, !((IHasUniques)it).HasUnique(UniqueType.WillNotBeChosenForNewGames))).ToList(),
            ruleset.Eras.Values.Select(it => new SetupEra(it.Name, it.EraNumber)).ToList(),
            new SetupMapOptions(
                MapShape.AllValues,
                MapType.AllValues,
                MapSize.Names(),
                MapResourceSettingExtensions.ActiveLabels(),
                [MirroringType.None, MirroringType.LeftRight, MirroringType.TopBottom, MirroringType.FourWay, MirroringType.AroundCenterTile],
                Enum.GetNames<MapGeneratorSteps>()),
            defaults,
            new MapParameters { BaseRuleset = effectiveBase, Mods = new LinkedHashSet<string>(defaults.Mods) });
    }
}
