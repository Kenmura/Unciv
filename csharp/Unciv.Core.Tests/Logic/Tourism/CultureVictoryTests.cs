using Unciv.Core.Tests.Logic.GreatWorks;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Tourism;
using Unciv.Models.Ruleset;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Tourism;

// B009 (bnw-spec/SPEC.md 4.3.4, 4.15): the culture victory milestone kinds and the Internet / Great Firewall influence maths.
[Collection("RulesetCache")]
public sealed class CultureVictoryTests
{
    private const string Spread100 = "[+100]% influence spread from Tourism";
    private const string Nullify = "Other civs' cultural influence is nullified here";

    private static GreatWorkWorld World(params string[] aUniques)
    {
        var w = new GreatWorkWorld(aUniques);
        var c = w.Game.Ruleset.ModOptions.Constants;
        c.BaseTourismPerGreatWork = 2;
        c.CultureLevelExotic = 10;
        c.CultureLevelFamiliar = 30;
        c.CultureLevelPopular = 60;
        c.CultureLevelInfluential = 100;
        c.CultureLevelDominant = 200;
        w.Game.Ruleset.Victories["Cultural"] = new Victory { Name = "Cultural", Milestones = ["Influential over all civilizations"] };
        w.Game.GameInfo.GameParameters.VictoryTypes.Add("Cultural");
        w.B.TotalCultureForContests = 1000;
        return w;
    }

    private static void Set(CivilizationObject me, CivilizationObject target, int influence) =>
        (me.Culture ??= new CivCultureManager()).GetOrAdd(target.CivId).Influence = influence;

    private static Milestone Culture(GreatWorkWorld w) => w.Game.Ruleset.Victories["Cultural"].MilestoneObjects[0];

    private static CivilizationObject ThirdCiv(GreatWorkWorld w, bool alive, bool met)
    {
        var c = w.Game.AddCiv(isPlayer: true);
        if (alive) w.Game.AddCity(c, w.Game.GetTile(4, -6));
        c.TotalCultureForContests = 1000;
        if (!met)
        {
            foreach (var other in w.Game.GameInfo.Civilizations.Where(it => it != c))
            {
                other.Diplomacy.Remove(c.CivId);
                c.Diplomacy.Remove(other.CivId);
            }
        }
        else w.A.DiplomacyFunctions.MakeCivilizationsMeet(c);
        return c;
    }

    // --- the milestone

    [Fact]
    public void TheMilestoneTextResolvesToItsKind()
    {
        var w = World();
        Assert.Equal(MilestoneType.InfluentialOverAllCivs, Culture(w).Type);
        Assert.Empty(Culture(w).Params);
        var vote = new Victory { Name = "V", Milestones = ["Win the World Leader vote"] }.MilestoneObjects[0];
        Assert.Equal(MilestoneType.WinWorldLeaderVote, vote.Type);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(999, false)] // 99%
    [InlineData(1000, true)] // exactly Influential
    [InlineData(2500, true)]
    public void CompletedExactlyAtTheInfluentialThreshold(int influence, bool done)
    {
        var w = World();
        Set(w.A, w.B, influence);
        Assert.Equal(done, Culture(w).HasBeenCompletedBy(w.A));
        Assert.Equal(done, w.A.VictoryManager.GetVictoryTypeAchieved() == "Cultural");
    }

    [Fact]
    public void EveryOtherLivingMajorCivCountsMetOrNot()
    {
        var w = World();
        Set(w.A, w.B, 1000);
        var stranger = ThirdCiv(w, alive: true, met: false);
        Assert.False(Culture(w).HasBeenCompletedBy(w.A)); // never met: influence 0
        Set(w.A, stranger, 1000);
        Assert.True(Culture(w).HasBeenCompletedBy(w.A));
        Set(w.A, stranger, 999);
        Assert.False(Culture(w).HasBeenCompletedBy(w.A));
    }

    [Fact]
    public void DeadCivsAndCityStatesDoNotCount()
    {
        var w = World();
        Set(w.A, w.B, 1000);
        var dead = ThirdCiv(w, alive: false, met: true);
        Assert.True(dead.IsDefeated());
        var cityState = w.Game.AddCiv(cityStateType: "Cultured");
        w.Game.AddCity(cityState, w.Game.GetTile(0, -8));
        Assert.True(Culture(w).HasBeenCompletedBy(w.A));
        Assert.Equal([w.B], InfluenceRules.VictoryRivals(w.A));
    }

    [Fact]
    public void NoRivalMeansNoCultureVictory()
    {
        var w = World();
        foreach (var city in w.B.Cities.ToList()) city.DestroyCity(overrideSafeties: true);
        Assert.True(w.B.IsDefeated());
        Assert.False(Culture(w).HasBeenCompletedBy(w.A));
    }

    [Fact]
    public void ZeroLifetimeCultureTargetCannotBeInfluenced()
    {
        var w = World();
        w.B.TotalCultureForContests = 0;
        Set(w.A, w.B, 5000);
        Assert.False(Culture(w).HasBeenCompletedBy(w.A));
    }

    [Fact]
    public void AGameWithoutTourismNeverCompletesIt()
    {
        var w = new GreatWorkWorld(enabled: false);
        w.Game.Ruleset.ModOptions.Constants.CultureLevelInfluential = 100;
        w.B.TotalCultureForContests = 1000;
        Set(w.A, w.B, 5000);
        var milestone = new Victory { Name = "C", Milestones = ["Influential over all civilizations"] }.MilestoneObjects[0];
        Assert.False(milestone.HasBeenCompletedBy(w.A));
    }

    [Fact]
    public void FocusHeaderAndProgressCountTheCivsWeAreInfluentialOver()
    {
        var w = World();
        var third = ThirdCiv(w, alive: true, met: true);
        Set(w.A, w.B, 1000);
        Set(w.A, third, 10);
        var milestone = Culture(w);
        Assert.Equal(Victory.FocusType.Culture, milestone.GetFocus(w.A));
        Assert.Equal(1, InfluenceRules.InfluentialCount(w.A));
        Assert.Equal("{Influential over all civilizations} (1/2)", milestone.GetVictoryScreenButtonHeaderText(false, w.A));
        Assert.Equal("{Influential over all civilizations} (2/2)", milestone.GetVictoryScreenButtonHeaderText(true, w.A));
        var line = milestone.GetFormattedLine();
        Assert.Equal("Influential over all civilizations", line.Text);
    }

    [Fact]
    public void TheWorldLeaderVoteMilestoneFollowsTheDiplomaticVoteFlag()
    {
        var w = World();
        var milestone = new Victory { Name = "D", Milestones = ["Win the World Leader vote"] }.MilestoneObjects[0];
        Assert.False(milestone.HasBeenCompletedBy(w.A));
        w.A.VictoryManager.HasEverWonDiplomaticVote = true;
        Assert.True(milestone.HasBeenCompletedBy(w.A));
        Assert.Equal(Victory.FocusType.CityStates, milestone.GetFocus(w.A));
        Assert.Equal("Win the World Leader vote", milestone.GetVictoryScreenButtonHeaderText(false, w.A));
    }

    [Fact]
    public void ARulesetWithoutLevelThresholdsNeverCompletesIt()
    {
        var w = World();
        w.Game.Ruleset.ModOptions.Constants.CultureLevelExotic = 0;
        Set(w.A, w.B, 100000);
        Assert.False(Culture(w).HasBeenCompletedBy(w.A));
    }

    [Fact]
    public void ANegativeSpreadSubtractsFromTheModifier()
    {
        var w = World(Spread100, "[-30]% influence spread from Tourism");
        Assert.Equal(70, TourismRules.ModifierWith(w.A, w.B));
    }

    // --- the Internet and the Great Firewall

    private static void GiveFirewall(GreatWorkWorld w, CivilizationObject owner)
    {
        var city = owner.Cities[0];
        city.CityConstructions.AddBuilding(w.Game.CreateWonder(Nullify));
    }

    [Fact]
    public void TheInternetAddsItsPercentToTheModifierTowardEveryCiv()
    {
        var w = World(Spread100, "[+50]% influence spread from Tourism");
        Assert.Equal(150, TourismRules.ModifierWith(w.A, w.B));
        Assert.Equal(0, TourismRules.ModifierWith(w.B, w.A));
        Assert.Equal(150, InfluenceRules.TechSpreadModifier(w.A, w.B));
    }

    [Fact]
    public void TheModifierAddsToTheOthersAndInfluenceGrows()
    {
        var w = World(Spread100);
        w.Game.Ruleset.ModOptions.Constants.TourismModifierOpenBorders = 25;
        var building = w.Build(w.CapA, 3, GreatWorkSlotType.ArtArtifact);
        string[] names = ["ArtAncient", "ArtClassical", "ArtMedieval"];
        for (int i = 0; i < 3; i++) w.Place(w.CapA, building, i, names[i], w.A.CivId); // 6 tourism
        w.A.GetDiplomacyManager(w.B)!.HasOpenBorders = true;
        Assert.Equal(125, TourismRules.ModifierWith(w.A, w.B));
        InfluenceRules.StartTurn(w.A);
        Assert.Equal(6 * 225 / 100, InfluenceRules.InfluenceOn(w.A, w.B)); // 13
    }

    [Fact]
    public void TheGreatFirewallNullifiesTheTechModifierAgainstItsOwnerOnly()
    {
        var w = World(Spread100);
        GiveFirewall(w, w.B);
        Assert.True(InfluenceRules.NullifiesForeignInfluence(w.B));
        Assert.False(InfluenceRules.NullifiesForeignInfluence(w.A));
        Assert.Equal(0, TourismRules.ModifierWith(w.A, w.B)); // A's Internet is nullified toward B
        var third = ThirdCiv(w, alive: true, met: true);
        Assert.Equal(100, TourismRules.ModifierWith(w.A, third)); // but not toward the others
    }

    [Fact]
    public void TheFirewallDoesNotRemoveTheOtherModifiers()
    {
        var w = World(Spread100);
        w.Game.Ruleset.ModOptions.Constants.TourismModifierOpenBorders = 25;
        w.A.GetDiplomacyManager(w.B)!.HasOpenBorders = true;
        GiveFirewall(w, w.B);
        Assert.Equal(25, TourismRules.ModifierWith(w.A, w.B));
    }

    [Fact]
    public void TheOwnersOwnInternetIsNotNullifiedByItsFirewall()
    {
        var w = World(Spread100);
        GiveFirewall(w, w.A);
        Assert.Equal(100, TourismRules.ModifierWith(w.A, w.B));
    }

    [Fact]
    public void TheBnwRulesetHasTheCulturalVictoryTheInternetAndTheFirewall()
    {
        var r = Models.Ruleset.BnwRulesetTests.LoadBnw();
        var cultural = r.Victories["Cultural"];
        Assert.Equal(["Influential over all civilizations"], cultural.Milestones);
        Assert.Equal(MilestoneType.InfluentialOverAllCivs, cultural.MilestoneObjects[0].Type);
        Assert.False(r.Victories.ContainsKey("Diplomatic"));
        Assert.Contains(Spread100, r.Technologies["The Internet"].Uniques);
        Assert.Contains(Nullify, r.Buildings["Great Firewall"].Uniques);
    }
}
