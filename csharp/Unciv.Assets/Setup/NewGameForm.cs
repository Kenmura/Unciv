using System.Globalization;
using Unciv.Assets;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Queries.Setup;
using Unciv.Utils;

namespace Unciv.GodotClient.Setup;

/// <summary>One line of the player list: the nation chosen (a nation name or <see cref="Constants.Random"/>) and whether a person plays it.</summary>
public sealed record PlayerSlot(string Nation, bool IsHuman);

/// <summary>C072: what the nation detail pane shows of a nation: its leader, its unique ability (name and text) and the terrain it prefers to start on.</summary>
public sealed record NationDetail(string Name, string Leader, string UniqueName, string UniqueText, IReadOnlyList<string> StartBias);

/// <summary>
/// What <see cref="NewGameForm.Build"/> produced: the setup and the seed to start it with, or the reasons there is none. A refusal is a result, never an exception.
/// </summary>
public sealed record FormResult(GameSetupInfo? Setup, long Seed, IReadOnlyList<string> Errors)
{
    public bool Ok => Setup != null;
}

/// <summary>
/// C015: the state behind the new-game screen, with no UI in it. It starts from the defaults of <see cref="NewGameOptions"/> (T199) for a base ruleset and mods,
/// changes only through methods that refuse a bad value with a message (and leave the form as it was), and turns into a <see cref="GameSetupInfo"/> with
/// <see cref="Build"/>, which also runs the engine's own <see cref="ValidateSetupQuery"/>. Nothing here reads a clock or draws a random number: a blank seed
/// is resolved from the clock the caller passes in. The widgets are a separate component so the host screen (C034) can reuse them.
/// Deliberate deviations from Kotlin: single player has exactly one human slot (choosing another human moves it; hotseat is C029), the spectator nation is
/// not offered, and seed text is validated instead of silently ignored.
/// </summary>
public sealed class NewGameForm
{
    public const int MinPlayers = 2;
    public const int DefaultPlayers = 4;

    private readonly Func<string?, IReadOnlyList<string>, NewGameOptions> optionsFor;
    private readonly List<PlayerSlot> slots = [];
    private readonly List<string> mods = [];
    private Ruleset? colourRuleset;
    private readonly HashSet<string> victories = new(StringComparer.Ordinal);

    public NewGameForm(Func<string?, IReadOnlyList<string>, NewGameOptions> optionsFor, int maxHumans = 1)
    {
        this.optionsFor = optionsFor;
        MaxHumans = maxHumans;
        Options = optionsFor(null, []);
        BaseRuleset = Options.BaseRuleset;
        ApplyDefaults();
    }

    /// <summary>A form reading the rulesets loaded in the engine's <c>RulesetCache</c>.</summary>
    public static NewGameForm FromRulesetCache(int maxHumans = 1) =>
        new((baseRuleset, mods) => new NewGameOptionsQuery(baseRuleset, mods).Run(), maxHumans);

    public int MaxHumans { get; }

    public NewGameOptions Options { get; private set; }

    public string BaseRuleset { get; private set; }

    public IReadOnlyList<string> Mods => mods;

    public IReadOnlyList<PlayerSlot> Slots => slots;

    public int CityStates { get; private set; }

    public string Difficulty { get; set; } = "";

    public string Speed { get; set; } = "";

    public string Era { get; set; } = "";

    public string MapType { get; set; } = "";

    public string Shape { get; set; } = "";

    public string Size { get; set; } = "";

    public string Resources { get; set; } = "";

    public bool WorldWrap { get; set; }

    public bool NoRuins { get; set; }

    public bool NoNaturalWonders { get; set; }

    /// <summary>The seed as typed: empty (or 0) means a seed taken from the clock when the game starts.</summary>
    public string SeedText { get; set; } = "";

    public bool NoBarbarians { get; set; }

    public bool RagingBarbarians { get; set; }

    public bool OneCityChallenge { get; set; }

    public bool NoCityRazing { get; set; }

    public bool NuclearWeapons { get; set; } = true;

    public bool Espionage { get; set; } = true;

    /// <summary>The victory names switched on.</summary>
    public IReadOnlyCollection<string> Victories => victories;

    /// <summary>The nations a player slot may take, in ruleset order (major civs only: the spectator is not a single-player choice).</summary>
    public IReadOnlyList<string> PlayableNations =>
        Options.Nations.Where(it => it.Choosable && it.IsMajorCiv && !it.IsSpectator).Select(it => it.Name).ToList();

    public int MaxPlayers => PlayableNations.Count;

    public int MaxCityStates => Options.Nations.Count(it => it.IsCityState);

    public IReadOnlyList<string> SelectableVictories => Options.Victories.Where(it => it.Selectable).Select(it => it.Name).ToList();

    /// <summary>Extension mods that can be switched on, by name.</summary>
    public IReadOnlyList<string> AvailableMods => Options.AvailableRulesets.Where(it => !it.IsBaseRuleset).Select(it => it.Name).ToList();

    public IReadOnlyList<string> BaseRulesets => Options.AvailableRulesets.Where(it => it.IsBaseRuleset).Select(it => it.Name).ToList();

    public int Humans => slots.Count(it => it.IsHuman);

    // Players

    /// <summary>Grows the list with random AI slots, or shrinks it from the end, removing AI slots before human ones. Returns a message when <paramref name="count"/> is out of range.</summary>
    public string? SetPlayerCount(int count)
    {
        if (count < MinPlayers || count > MaxPlayers)
            return $"The number of players must be between {MinPlayers.ToString(CultureInfo.InvariantCulture)} and {MaxPlayers.ToString(CultureInfo.InvariantCulture)}.";
        while (slots.Count < count) slots.Add(new PlayerSlot(Constants.Random, false));
        while (slots.Count > count)
        {
            int last = slots.FindLastIndex(it => !it.IsHuman);
            slots.RemoveAt(last >= 0 ? last : slots.Count - 1);
        }
        return null;
    }

    /// <summary>Gives the slot a nation or <see cref="Constants.Random"/>. A nation another slot already has, or one that cannot be played, is refused.</summary>
    public string? SetNation(int slot, string nation)
    {
        if (!IsSlot(slot)) return SlotMessage(slot);
        if (nation != Constants.Random)
        {
            if (!PlayableNations.Contains(nation)) return $"{nation} is not a nation you can play in this ruleset.";
            int taken = slots.FindIndex(it => it.Nation == nation);
            if (taken >= 0 && taken != slot) return $"{nation} is already player {(taken + 1).ToString(CultureInfo.InvariantCulture)}.";
        }
        slots[slot] = slots[slot] with { Nation = nation };
        return null;
    }

    /// <summary>Makes the slot human or AI. With the human limit reached, the other humans (first ones first) become AI so the limit holds.</summary>
    public string? SetHuman(int slot, bool human)
    {
        if (!IsSlot(slot)) return SlotMessage(slot);
        if (human)
        {
            for (int i = 0; i < slots.Count && Humans - (slots[slot].IsHuman ? 1 : 0) >= MaxHumans; i++)
            {
                if (i != slot && slots[i].IsHuman) slots[i] = slots[i] with { IsHuman = false };
            }
        }
        slots[slot] = slots[slot] with { IsHuman = human };
        return null;
    }

    public string? SetCityStates(int count)
    {
        if (count < 0 || count > MaxCityStates)
            return $"The number of city-states must be between 0 and {MaxCityStates.ToString(CultureInfo.InvariantCulture)}.";
        CityStates = count;
        return null;
    }

    // Victories and rulesets

    public string? SetVictory(string name, bool on)
    {
        if (!SelectableVictories.Contains(name)) return $"{name} is not a victory type of this ruleset.";
        if (on) victories.Add(name);
        else victories.Remove(name);
        return null;
    }

    public string? SetBaseRuleset(string name)
    {
        if (!BaseRulesets.Contains(name)) return $"{name} is not a base ruleset.";
        return Reload(name, mods);
    }

    public string? SetMod(string name, bool on)
    {
        if (!AvailableMods.Contains(name)) return $"{name} is not a mod that can be used.";
        var next = mods.Where(it => it != name).ToList();
        if (on) next.Add(name);
        return Reload(BaseRuleset, next);
    }

    // The ruleset changed: ask again, keep every choice that is still valid, fall back to the new defaults for the rest.
    private string? Reload(string baseRuleset, List<string> newMods)
    {
        NewGameOptions fresh;
        try
        {
            fresh = optionsFor(baseRuleset, newMods);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return "These rulesets cannot be combined: " + e.Message;
        }
        Options = fresh;
        colourRuleset = null;
        BaseRuleset = fresh.BaseRuleset;
        mods.Clear();
        mods.AddRange(fresh.Mods);
        var d = fresh.DefaultGameParameters;
        if (!fresh.Difficulties.Any(it => it.Name == Difficulty)) Difficulty = d.Difficulty;
        if (!fresh.Speeds.Any(it => it.Name == Speed)) Speed = d.Speed;
        if (!fresh.Eras.Any(it => it.Name == Era)) Era = d.StartingEra;
        var playable = PlayableNations;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Nation != Constants.Random && !playable.Contains(slots[i].Nation)) slots[i] = slots[i] with { Nation = Constants.Random };
        }
        if (slots.Count > MaxPlayers && MaxPlayers >= MinPlayers) SetPlayerCount(MaxPlayers);
        CityStates = Math.Min(CityStates, MaxCityStates);
        victories.IntersectWith(SelectableVictories);
        if (victories.Count == 0) victories.UnionWith(d.VictoryTypes);
        return null;
    }

    // Colours

    /// <summary>The colours a nation is shown in (background, then text on it) from the current ruleset; null for a name the ruleset does not have (and for <see cref="Constants.Random"/>).</summary>
    public (Rgb Background, Rgb Text)? NationColours(string nation)
    {
        colourRuleset ??= RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(mods), BaseRuleset);
        if (!colourRuleset.Nations.TryGetValue(nation, out var found)) return null;
        return (ToRgb(found.GetOuterColor()), ToRgb(found.GetInnerColor()));
    }

    /// <summary>C072: the detail of a nation of the current ruleset; null for a name it does not have (and for <see cref="Constants.Random"/>).</summary>
    public NationDetail? DescribeNation(string nation)
    {
        colourRuleset ??= RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(mods), BaseRuleset);
        if (!colourRuleset.Nations.TryGetValue(nation, out var found)) return null;
        return new NationDetail(found.Name, found.LeaderName, found.UniqueName, found.UniqueText, found.StartBias.ToList());
    }

    /// <summary>C072: the one-line summary shown in the screen's footer: players, city-states, difficulty, speed and the map.</summary>
    public string Summary =>
        $"{Slots.Count.ToString(CultureInfo.InvariantCulture)} players, {CityStates.ToString(CultureInfo.InvariantCulture)} city-states, {Difficulty}, {Speed}, {MapType} {Size}";

    private static Rgb ToRgb(EngineColor c) => new(Channel(c.R), Channel(c.G), Channel(c.B));

    private static byte Channel(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f, MidpointRounding.AwayFromZero), 0, 255);

    // Build

    /// <summary>
    /// The mistakes in the form that can be found without loading rulesets: values outside the lists, counts out of range, a seed that is not a number, too many humans.
    /// Cheap enough to show after every change.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (slots.Count < MinPlayers || slots.Count > MaxPlayers)
            problems.Add($"The number of players must be between {MinPlayers.ToString(CultureInfo.InvariantCulture)} and {MaxPlayers.ToString(CultureInfo.InvariantCulture)}.");
        if (Humans > MaxHumans) problems.Add($"At most {MaxHumans.ToString(CultureInfo.InvariantCulture)} human player(s) can play here.");
        if (Humans == 0) problems.Add("No human players selected!");
        if (CityStates < 0 || CityStates > MaxCityStates)
            problems.Add($"The number of city-states must be between 0 and {MaxCityStates.ToString(CultureInfo.InvariantCulture)}.");
        if (victories.Count == 0) problems.Add("No victory conditions were selected!");
        if (!Options.Difficulties.Any(it => it.Name == Difficulty)) problems.Add($"Unknown difficulty {Difficulty}");
        if (!Options.Speeds.Any(it => it.Name == Speed)) problems.Add($"Unknown game speed {Speed}");
        if (!Options.Eras.Any(it => it.Name == Era)) problems.Add($"Unknown starting era {Era}");
        if (!Options.MapOptions.Types.Contains(MapType)) problems.Add($"Unknown map type {MapType}");
        if (!Options.MapOptions.Shapes.Contains(Shape)) problems.Add($"Unknown map shape {Shape}");
        if (!Options.MapOptions.Sizes.Contains(Size)) problems.Add($"Unknown map size {Size}");
        if (!Options.MapOptions.Resources.Contains(Resources)) problems.Add($"Unknown resource setting {Resources}");
        if (!TryParseSeed(SeedText, out _, out string? seedProblem)) problems.Add(seedProblem ?? "");
        return problems;
    }

    /// <summary>
    /// Seed text to a seed: blank is 0 (take the seed from the clock), otherwise a whole number from 0 up. The error is a message for the player.
    /// </summary>
    public static bool TryParseSeed(string? text, out long seed, out string? problem)
    {
        seed = 0;
        problem = null;
        string trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return true;
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out seed)) return true;
        seed = 0;
        problem = "The seed must be a whole number from 0 to " + long.MaxValue.ToString(CultureInfo.InvariantCulture) + " (leave it empty for a random map).";
        return false;
    }

    /// <summary>
    /// The setup the form describes and the seed to start it with (the typed seed, or <paramref name="clockMillis"/> when none was typed or it was 0; it is also the map seed, so
    /// the saved map records it). The engine's <see cref="ValidateSetupQuery"/> has the last word; every error is returned, none is thrown. Each call builds a fresh setup.
    /// </summary>
    public FormResult Build(long clockMillis)
    {
        var problems = Problems();
        if (problems.Count > 0) return new FormResult(null, 0, problems);
        TryParseSeed(SeedText, out long typed, out _);
        long seed = typed != 0 ? typed : clockMillis;

        var gameParameters = new GameParameters
        {
            BaseRuleset = BaseRuleset,
            Mods = new LinkedHashSet<string>(mods),
            Difficulty = Difficulty,
            Speed = Speed,
            StartingEra = Era,
            Players = slots.Select(it => new Player(it.Nation, it.IsHuman ? PlayerType.Human : PlayerType.AI)).ToList(),
            NumberOfCityStates = CityStates,
            VictoryTypes = Options.Victories.Where(it => victories.Contains(it.Name)).Select(it => it.Name).ToList(),
            NoBarbarians = NoBarbarians,
            RagingBarbarians = RagingBarbarians,
            OneCityChallenge = OneCityChallenge,
            NoCityRazing = NoCityRazing,
            NuclearWeaponsEnabled = NuclearWeapons,
            EspionageEnabled = Espionage,
        };
        var mapParameters = new MapParameters
        {
            BaseRuleset = BaseRuleset,
            Mods = new LinkedHashSet<string>(mods),
            Type = MapType,
            Shape = Shape,
            MapSize = new MapSize(Size),
            MapResources = Resources,
            WorldWrap = WorldWrap,
            NoRuins = NoRuins,
            NoNaturalWonders = NoNaturalWonders,
            Seed = seed,
        };
        var setup = new GameSetupInfo(gameParameters, mapParameters);

        SetupValidation validation;
        try
        {
            validation = new ValidateSetupQuery(setup).Run();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new FormResult(null, 0, ["This combination of rulesets cannot be checked: " + e.Message]);
        }
        return validation.Ok
            ? new FormResult(setup, seed, [])
            : new FormResult(null, 0, validation.Errors.Select(it => it.Message).ToList());
    }

    private bool IsSlot(int slot) => slot >= 0 && slot < slots.Count;

    private string SlotMessage(int slot) => $"There is no player {(slot + 1).ToString(CultureInfo.InvariantCulture)}.";

    private void ApplyDefaults()
    {
        var d = Options.DefaultGameParameters;
        var map = Options.DefaultMapParameters;
        mods.Clear();
        mods.AddRange(Options.Mods);
        Difficulty = d.Difficulty;
        Speed = d.Speed;
        Era = d.StartingEra;
        victories.Clear();
        victories.UnionWith(d.VictoryTypes);
        MapType = map.Type;
        Shape = MapShape.Rectangular;
        Size = MapSize.Names().Contains("Small") ? "Small" : Options.MapOptions.Sizes.FirstOrDefault() ?? "";
        Resources = map.MapResources;
        WorldWrap = true;
        slots.Clear();
        slots.Add(new PlayerSlot(Constants.Random, true));
        for (int i = 1; i < Math.Min(DefaultPlayers, Math.Max(MaxPlayers, 1)); i++) slots.Add(new PlayerSlot(Constants.Random, false));
        CityStates = Math.Min(d.NumberOfCityStates, MaxCityStates);
    }
}
