using Unciv.Commands;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Files;
using Unciv.Logic.GreatWorks;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.GreatWorks;

// B007: a game with Great Works switched on. A (the current player) and B are humans who have met. A has the capital CapA and a second city
// CityA2, B has CityB. The ruleset has these works (era in brackets, "" = the creator's era):
//   Art: ArtAncient, ArtClassical, ArtMedieval, ArtAnyEra ("")         Artifact: FindAncient, FindClassical
//   Writing: WriteAncient, WriteClassical, HiddenWriting (archaeology only)    Music: MusicAncient
internal sealed class GreatWorkWorld
{
    public const string Ancient = "Ancient era";
    public const string Classical = "Classical era";
    public const string Medieval = "Medieval era";

    public TestGame Game { get; } = new();
    public CivilizationObject A { get; }
    public CivilizationObject B { get; }
    public CityObject CapA { get; }
    public CityObject CityA2 { get; }
    public CityObject CityB { get; }
    public GameSession Session { get; }

    public GreatWorkWorld(string[]? aUniques = null, bool enabled = true, bool archaeology = false)
    {
        if (enabled) Game.Ruleset.ModOptions.Uniques.Add("Enables Great Works and tourism");
        if (archaeology) Game.Ruleset.ModOptions.Uniques.Add("Enables archaeology");
        Game.Ruleset.ModOptions.Constants.BaseCulturePerGreatWork = 2;
        AddWork("ArtAncient", GreatWorkClass.Art, Ancient);
        AddWork("ArtClassical", GreatWorkClass.Art, Classical);
        AddWork("ArtMedieval", GreatWorkClass.Art, Medieval);
        AddWork("ArtAnyEra", GreatWorkClass.Art, "");
        AddWork("FindAncient", GreatWorkClass.Artifact, Ancient);
        AddWork("FindClassical", GreatWorkClass.Artifact, Classical);
        AddWork("WriteAncient", GreatWorkClass.Writing, Ancient);
        AddWork("WriteClassical", GreatWorkClass.Writing, Classical);
        AddWork("HiddenWriting", GreatWorkClass.Writing, Ancient, archaeologyOnly: true);
        AddWork("MusicAncient", GreatWorkClass.Music, Ancient);

        A = Game.AddCiv(aUniques, isPlayer: true);
        B = Game.AddCiv(isPlayer: true);
        Game.GameInfo.EngineOptions.VisibleMap = true;
        Game.MakeHexagonalMap(12);
        CapA = Game.AddCity(A, Game.GetTile(-4, 0));
        CityA2 = Game.AddCity(A, Game.GetTile(-4, 6));
        CityB = Game.AddCity(B, Game.GetTile(4, 0));
        A.DiplomacyFunctions.MakeCivilizationsMeet(B);
        foreach (var civ in new[] { A, B }) civ.PopupAlerts.Clear();
        Game.GameInfo.CurrentPlayerCiv = A;
        Game.GameInfo.CurrentPlayer = A.CivId;
        Session = new GameSession(Game.GameInfo);
    }

    private void AddWork(string name, GreatWorkClass greatWorkClass, string era, bool archaeologyOnly = false) =>
        Game.Ruleset.GreatWorks.Put(name, new GreatWork { Name = name, GreatWorkClass = greatWorkClass, Era = era, ArchaeologyOnly = archaeologyOnly });

    /// <summary>A new building with slots, not yet built anywhere.</summary>
    public Building MakeBuilding(int slots, GreatWorkSlotType type, params ThemingBonus[] rows)
    {
        var building = Game.CreateBuilding();
        building.GreatWorkSlots = slots;
        building.GreatWorkSlotType = type;
        building.ThemingBonuses = [.. rows];
        return building;
    }

    public Building Build(CityObject city, int slots, GreatWorkSlotType type, params ThemingBonus[] rows)
    {
        var building = MakeBuilding(slots, type, rows);
        city.CityConstructions.AddBuilding(building);
        return building;
    }

    /// <summary>A and B have met and are at peace (the default after meeting is war).</summary>
    public void MakePeace()
    {
        A.GetDiplomacyManager(B)!.DiplomaticStatus = DiplomaticStatus.Peace;
        B.GetDiplomacyManager(A)!.DiplomaticStatus = DiplomaticStatus.Peace;
    }

    /// <summary>Registers a created work (as creator <paramref name="creator"/>, era <paramref name="era"/>) and puts it into a slot.</summary>
    public void Place(CityObject city, Building building, int slot, string work, string creator = "", string era = Ancient)
    {
        var instances = Game.GameInfo.GreatWorkInstances ??= [];
        instances.RemoveAll(it => it.Name == work);
        instances.Add(new GreatWorkInstance { Name = work, Creator = creator, Era = era });
        city.SetGreatWorkIn(building.Name, slot, work);
    }

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
}
