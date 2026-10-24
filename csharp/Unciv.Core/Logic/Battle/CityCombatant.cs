using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/CityCombatant.kt (T081). Purity annotations dropped.
// Kotlin CityCombatant has no equals/hashCode (reference identity), as here.
public sealed class CityCombatant(CityObject city) : ICombatant
{
    public CityObject City { get; } = city;

    public int GetMaxHealth() => City.GetMaxHealth();
    public int GetHealth() => City.Health;
    public CivilizationObject GetCivInfo() => City.Civ;
    public TileClass GetTile() => City.GetCenterTile();
    public string GetName() => City.Name;
    public bool IsDefeated() => City.Health == 1;
    public bool IsVisibleTo(CivilizationObject to) => true;
    public bool CanAttack() => City.CanBombard();

    public bool MatchesFilter(string filter, bool multiFilter = true) =>
        multiFilter
            ? MultiFilter.Filter(filter, it => it == "City" || Constants.All.Contains(it) || City.MatchesFilter(it, City.Civ, false))
            : filter == "City" || Constants.All.Contains(filter) || City.MatchesFilter(filter, City.Civ, false);

    public UncivSound GetAttackSound() => UncivSound.Bombard;

    public void TakeDamage(int damage)
    {
        City.Health = unchecked(City.Health - damage);
        if (City.Health < 1) City.Health = 1; // min health is 1
    }

    public UnitType GetUnitType() => UnitType.City;

    public int GetAttackingStrength(ICombatant? defender = null) =>
        KotlinMath.RoundToInt(GetCityStrength(defender, CombatAction.Attack) * 0.75);

    public int GetDefendingStrength(ICombatant? attacker = null)
    {
        if (IsDefeated()) return 1;
        return GetCityStrength(attacker);
    }

    // Civ fanatics forum, from a modder who went through the original code
    public int GetCityStrength(ICombatant? theirCombatant = null, CombatAction combatAction = CombatAction.Defend)
    {
        var modConstants = GetCivInfo().GameInfo.Ruleset.ModOptions.Constants;
        double strength = modConstants.CityStrengthBase;
        strength += City.Population.Population * modConstants.CityStrengthPerPop; // Each 5 pop gives 2 defence
        var cityTile = City.GetCenterTile();
        foreach (var terrain in cityTile.AllTerrains)
            ((IHasUniques)terrain).ForEachMatchingUnique(UniqueType.GrantsCityStrength, GameContext.EmptyState, unique =>
            {
                strength += unique.Params[0].ToInt();
            });
        // as tech progresses so does city strength
        int techCount = GetCivInfo().GameInfo.Ruleset.Technologies.Count;
        float techsPercentKnown = techCount > 0 ? (float)City.Civ.Tech.TechsResearched.Count / techCount : 0.5f; // for mods with no tech
        strength += Math.Pow(techsPercentKnown * modConstants.CityStrengthFromTechsMultiplier, modConstants.CityStrengthFromTechsExponent)
            * modConstants.CityStrengthFromTechsFullMultiplier;

        // The way all of this adds up...
        // All ancient techs - 0.5 extra, Classical - 2.7, Medieval - 8, Renaissance - 17.5,
        // Industrial - 32.4, Modern - 51, Atomic - 72.5, All - 118.3

        // Garrisoned unit gives up to 20% of strength to city, health-dependant
        if (cityTile.MilitaryUnit != null)
            strength += cityTile.MilitaryUnit.BaseUnit.Strength * (cityTile.MilitaryUnit.Health / 100f) * modConstants.CityStrengthFromGarrison;

        float buildingsStrength = City.GetStrength();
        var gameContext = new GameContext(GetCivInfo(), City, ourCombatant: this, theirCombatant: theirCombatant, combatAction: combatAction);

        GetCivInfo().ForEachMatchingUnique(UniqueType.BetterDefensiveBuildings, gameContext, unique =>
        {
            buildingsStrength *= unique.Params[0].ToPercent();
        });
        strength += buildingsStrength;

        int extraStrength = 0;
        City.ForEachMatchingUnique(UniqueType.StrengthAmount, gameContext, it => extraStrength = unchecked(extraStrength + it.Params[0].ToInt()));
        strength += extraStrength;

        return KotlinMath.RoundToInt(strength);
    }

    public IEnumerable<UniqueObject> GetTriggeredUniques(UniqueType trigger, GameContext gameContext, Func<UniqueObject, bool>? triggerFilter = null) =>
        City.GetTriggeredUniques(trigger, gameContext, triggerFilter);

    public override string ToString() => City.Name; // for debug
}
