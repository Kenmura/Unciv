using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Logic;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Commands.Empire;

// T192: a valid sample of each empire command plus the state it needs, for GameCommandTests (which gives every command a fresh game
// because the samples are mutually exclusive: a pending free tech blocks the queue, a founded pantheon blocks founding one, ...).
internal static class EmpireSamples
{
    public static readonly string[] Types =
    [
        "set-research-queue", "pick-free-technology", "debug-add-technology", "adopt-policy", "found-pantheon", "choose-beliefs",
        "found-religion", "choose-free-great-person", "move-spy", "vote-diplomatic", "ack-diplomatic-vote-result", "set-gold-to-science",
    ];

    private static Belief FreeBelief(GameInfo game, BeliefType type) =>
        game.Ruleset.Beliefs.Values.First(it => it.Type == type && game.GetCivilization("Babylon").ReligionManager.GetReligionWithBelief(it) == null);

    // The vanilla ruleset has no religion data.
    private static void AddReligionData(GameInfo game)
    {
        foreach (var (name, beliefType) in new[]
        {
            ("Pantheon 1", BeliefType.Pantheon), ("Pantheon 2", BeliefType.Pantheon), ("Founder 1", BeliefType.Founder),
            ("Follower 1", BeliefType.Follower), ("Follower 2", BeliefType.Follower),
        })
            game.Ruleset.Beliefs[name] = new Belief(beliefType) { Name = name };
        game.Ruleset.Religions.AddRange(["Islam", "Judaism", "Taoism", "Shinto", "Sikhism", "Hinduism", "Buddhism"]);
        game.Ruleset.ModOptions = new ModOptions(); // vanilla disables religion there
    }

    public static GameCommand Sample(string type, GameInfo game)
    {
        if (type is "found-pantheon" or "choose-beliefs" or "found-religion") AddReligionData(game);
        var babylon = game.GetCivilization("Babylon");
        switch (type)
        {
            case "set-research-queue":
                return new SetResearchQueue { Techs = ["Pottery"] };
            case "pick-free-technology":
                babylon.Tech.FreeTechs = 1;
                return new PickFreeTechnology { Tech = "Pottery" };
            case "debug-add-technology":
                game.GameParameters.GodMode = true;
                return new DebugAddTechnology { Tech = "Pottery" };
            case "adopt-policy":
                babylon.Policies.StoredCulture = 1000;
                return new AdoptPolicy { Policy = "Tradition" };
            case "found-pantheon":
                babylon.ReligionManager.StoredFaith = 1000;
                return new FoundPantheon { Belief = FreeBelief(game, BeliefType.Pantheon).Name };
            case "choose-beliefs":
                babylon.ReligionManager.ChooseBeliefs([FreeBelief(game, BeliefType.Pantheon)], useFreeBeliefs: true);
                babylon.ReligionManager.FreeBeliefs["Follower"] = 1;
                return new ChooseBeliefs { Beliefs = [FreeBelief(game, BeliefType.Follower).Name] };
            case "found-religion":
                babylon.ReligionManager.ChooseBeliefs([FreeBelief(game, BeliefType.Pantheon)], useFreeBeliefs: true);
                var city = babylon.Cities[0];
                // Any unit standing in the city will do as the "prophet" (vanilla has no Great Prophet).
                babylon.ReligionManager.FoundReligion(city.GetCenterTile().GetUnits().First());
                return new FoundReligion
                {
                    ReligionName = "Islam",
                    DisplayName = "Islam",
                    Beliefs = [FreeBelief(game, BeliefType.Founder).Name, FreeBelief(game, BeliefType.Follower).Name],
                };
            case "choose-free-great-person":
                babylon.GreatPeople.FreeGreatPeople = 1;
                return new ChooseFreeGreatPerson { Unit = "Great Scientist" };
            case "move-spy":
                game.GameParameters.EspionageEnabled = true;
                var spy = babylon.EspionageManager.AddSpy();
                var foreign = game.GetCivilization("Greece").Cities[0];
                foreign.GetCenterTile().SetExplored(babylon, true);
                return new MoveSpy { Spy = spy.Name, CityId = foreign.Id };
            case "vote-diplomatic":
                babylon.AddFlag(Unciv.Logic.Civilization.CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
                return new VoteDiplomatic();
            case "ack-diplomatic-vote-result":
                babylon.AddFlag(Unciv.Logic.Civilization.CivFlags.ShowDiplomaticVotingResults.ToString(), 0);
                return new AckDiplomaticVoteResult();
            case "set-gold-to-science":
                game.Ruleset.ModOptions = new ModOptions { Uniques = ["Can convert gold to science with sliders"] };
                return new SetGoldToScience { Percent = 30 };
            default:
                throw new InvalidOperationException(type);
        }
    }
}
