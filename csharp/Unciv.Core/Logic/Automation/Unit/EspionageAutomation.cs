using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Unit;

// Ported from core/src/com/unciv/logic/automation/unit/EspionageAutomation.kt (T087).
// Kotlin `Random(seed)` is kotlin.random.Random(Int), i.e. GameRandom(int). The sorted civs property
// `getCivsToStealFromSorted` is a plain (eagerly initialised) Kotlin property, so it is computed in the constructor and
// is therefore a snapshot from before the spies move, exactly as in Kotlin.

public class EspionageAutomation
{
    private readonly CivilizationClass civInfo;
    private readonly KotlinLazy<List<CivilizationClass>> civsToStealFromLazy;
    private readonly List<CivilizationClass> getCivsToStealFromSorted;
    private readonly KotlinLazy<List<CivilizationClass>> cityStatesToRigLazy;

    public EspionageAutomation(CivilizationClass civInfo)
    {
        this.civInfo = civInfo;
        civsToStealFromLazy = new KotlinLazy<List<CivilizationClass>>(() =>
            civInfo.GetKnownCivs().Where(otherCiv => otherCiv.IsMajorCiv()
                && otherCiv.Cities.Any(it => it.GetCenterTile().IsExplored(civInfo) && civInfo.EspionageManager.GetSpyAssignedToCity(it) == null)
                && civInfo.EspionageManager.GetTechsToSteal(otherCiv).Count != 0).ToList());
        getCivsToStealFromSorted = CivsToStealFrom
            .OrderBy(otherCiv => civInfo.EspionageManager.SpyList
                .Count(it => it.IsDoingWork() && it.GetCityOrNull()?.Civ == otherCiv))
            .ToList(); // OrderBy is stable, like Kotlin sortedBy
        cityStatesToRigLazy = new KotlinLazy<List<CivilizationClass>>(() =>
            civInfo.GetKnownCivs().Where(otherCiv => otherCiv.IsMinorCiv() && otherCiv.Knows(civInfo) && !civInfo.IsAtWarWith(otherCiv)).ToList());
    }

    public CivilizationClass CivInfo => civInfo;

    private List<CivilizationClass> CivsToStealFrom => civsToStealFromLazy.Value;

    private List<CivilizationClass> CityStatesToRig => cityStatesToRigLazy.Value;

    public void AutomateSpies()
    {
        var spies = civInfo.EspionageManager.SpyList;
        var spiesToMove = spies.Where(it => it.IsAlive() && !it.IsDoingWork()).ToList();
        foreach (var spy in spiesToMove)
        {
            var randomSeed = spies.Count + spies.IndexOf(spy) + civInfo.GameInfo.Turns;
            var randomAction = civInfo.GameInfo.SeededRandom(randomSeed).NextInt(10);

            // Try each operation based on the random value and the success rate
            // If an operation was not successfull try the next one
            var capital = civInfo.GetCapital();
            if (capital != null && !spies.Any(it => it.GetCityOrNull() == capital))
            {
                spy.MoveTo(capital);
                continue;
            }
            if (randomAction <= 7 && AutomateSpyStealTech(spy)) continue;
            if (randomAction <= 9 && AutomateSpyRigElection(spy)) continue;
            if (AutomateSpyCounterInteligence(spy)) continue;
            // We might have been doing counter intelligence and wanted to look for something better
            if (spy.IsDoingWork()) continue;
            // Retry initial operations one more time, without the randomAction check
            if (AutomateSpyStealTech(spy)) continue;
            if (AutomateSpyRigElection(spy)) continue;

            // There is nothing for our spy to do, put it in a random city
            var chosenCity = civInfo.GameInfo.GetCities().Where(it => spy.CanMoveTo(it))
                .MaxBy(it => it.CityStats.CurrentCityStats.Science);
            spy.MoveTo(chosenCity);
        }
        foreach (var spy in spies) CheckIfShouldStageCoup(spy);
    }

    /// <summary>Moves the spy to a city that we can steal a tech from</summary>
    private bool AutomateSpyStealTech(Spy spy)
    {
        if (CivsToStealFrom.Count == 0) return false;
        // We want to move the spy to the city with the highest science generation
        // Players can't usually figure this out so lets do highest population instead
        var cityToMoveTo = getCivsToStealFromSorted[0].Cities.Where(it => spy.CanMoveTo(it))
            .MaxBy(it => it.Population.Population);
        spy.MoveTo(cityToMoveTo);
        return cityToMoveTo != null;
    }

    /// <summary>Moves the spy to a random city-state</summary>
    private bool AutomateSpyRigElection(Spy spy)
    {
        var cityToMoveTo = CityStatesToRig.SelectMany(it => it.Cities)
            .Where(it => !it.IsBeingRazed && spy.CanMoveTo(it)
                && (it.Civ.GetDiplomacyManager(civInfo).NotNull().GetInfluence() < 150 || it.Civ.AllyCiv != civInfo))
            .MaxBy(it => it.Civ.GetDiplomacyManager(civInfo).NotNull().GetInfluence());
        spy.MoveTo(cityToMoveTo);
        return cityToMoveTo != null;
    }

    /// <summary>Moves the spy to a random city of ours</summary>
    private bool AutomateSpyCounterInteligence(Spy spy)
    {
        var cityToMoveTo = civInfo.Cities.Where(it => spy.CanMoveTo(it))
            .MaxBy(it => it.CityStats.CurrentCityStats.Science);
        if (cityToMoveTo == null) return false;
        spy.MoveTo(cityToMoveTo);
        return true;
    }

    private void CheckIfShouldStageCoup(Spy spy)
    {
        if (!spy.CanDoCoup()) return;
        if (spy.GetCoupChanceOfSuccess(false) < .7) return;
        var allyCiv = spy.GetCity().Civ.AllyCiv;
        // Don't coup city-states whose allies are out friends
        if (allyCiv != null && civInfo.GetDiplomacyManager(allyCiv)?.IsRelationshipLevelGE(RelationshipLevel.Friend) == true) return;
        var spies = civInfo.EspionageManager.SpyList;
        var randomSeed = spies.Count + spies.IndexOf(spy) + civInfo.GameInfo.Turns;
        var randomAction = civInfo.GameInfo.SeededRandom(randomSeed).NextInt(100);
        if (randomAction < 20) spy.SetAction(SpyAction.Coup, 1);
    }
}
