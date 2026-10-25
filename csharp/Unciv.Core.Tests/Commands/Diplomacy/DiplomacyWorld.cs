using Unciv.Commands;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Files;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Commands.Diplomacy;

// T193: A and B are humans (A is the current player), C is an AI, S a city-state; one city each, everybody has met everybody.
internal sealed class DiplomacyWorld
{
    public TestGame Game { get; } = new();
    public CivilizationObject A { get; }
    public CivilizationObject B { get; }
    public CivilizationObject C { get; }
    public CivilizationObject S { get; }
    public CityObject CityA { get; }
    public CityObject CityS { get; }
    public GameSession Session { get; }

    public DiplomacyWorld(string[]? aUniques = null, string[]? modUniques = null, string[]? bUniques = null)
    {
        if (modUniques != null) Game.Ruleset.ModOptions.Uniques.AddRange(modUniques);
        A = Game.AddCiv(aUniques, isPlayer: true);
        B = Game.AddCiv(bUniques, isPlayer: true);
        C = Game.AddCiv();
        S = Game.AddCiv(cityStateType: "Cultured");
        Game.GameInfo.EngineOptions.VisibleMap = true;
        Game.MakeHexagonalMap(12);
        CityA = Game.AddCity(A, Game.GetTile(-4, 0));
        Game.AddCity(B, Game.GetTile(4, 0));
        Game.AddCity(C, Game.GetTile(0, 6));
        CityS = Game.AddCity(S, Game.GetTile(0, -6), initialPopulation: 4);
        foreach (var x in Game.GameInfo.Civilizations)
            foreach (var y in Game.GameInfo.Civilizations)
                if (x != y && !x.IsBarbarian && !y.IsBarbarian) x.DiplomacyFunctions.MakeCivilizationsMeet(y);
        foreach (var civ in Game.GameInfo.Civilizations) civ.PopupAlerts.Clear(); // first contact and tech alerts
        Game.GameInfo.CurrentPlayerCiv = A;
        Game.GameInfo.CurrentPlayer = A.CivId;
        Session = new GameSession(Game.GameInfo);
    }

    public DiplomacyManager Diplo(CivilizationObject from, CivilizationObject to) => from.GetDiplomacyManager(to)!;

    public ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(Game.GameInfo, forceZip: false)));

    public CommandResult Ok(GameCommand command, string? actor = null)
    {
        var result = Session.Submit(actor ?? A.CivId, command);
        Assert.True(result.Accepted, result.Validation.Message);
        return result;
    }

    // A rejection must leave state and log untouched.
    public void Rejected(GameCommand command, string code, string? actor = null)
    {
        ulong before = Hash();
        int logged = Session.Log.Count;
        var result = Session.Submit(actor ?? A.CivId, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash());
        Assert.Equal(logged, Session.Log.Count);
    }

    public void MakeWar(CivilizationObject x, CivilizationObject y)
    {
        Diplo(x, y).DiplomaticStatus = DiplomaticStatus.War;
        Diplo(y, x).DiplomaticStatus = DiplomaticStatus.War;
    }
}
