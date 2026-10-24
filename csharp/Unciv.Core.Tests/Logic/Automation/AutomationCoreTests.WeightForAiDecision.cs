using Unciv.Core.Tests.Support;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Logic.Automation;

// T087: IHasUniques.getWeightForAiDecision (no Kotlin fixture; expected values derived by hand from the Kotlin source).
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class WeightForAiDecisionTests
    {
        private readonly TestGame testGame = new();

        [Fact]
        public void WeightIsTheProductOfAllAiChoiceWeightUniques()
        {
            var civ = testGame.AddCiv();
            var belief = testGame.CreateBelief(uniques: ["[+50]% weight to this choice for AI decisions", "[-50]% weight to this choice for AI decisions"]);
            Assert.Equal(1.5f * 0.5f, ((IHasUniques)belief).GetWeightForAiDecision(new GameContext(civ)));
            Assert.Equal(1f, ((IHasUniques)testGame.CreateBelief()).GetWeightForAiDecision(new GameContext(civ)));
        }

        [Fact]
        public void UnitsAndBuildingsAreAlsoScaledByThePersonalityOfAnAiCiv()
        {
            testGame.Ruleset.Personalities["Test personality"] = new Personality
            {
                Name = "Test personality",
                Uniques = ["[+100]% weight to [Melee] for AI decisions", "[-50]% weight to [Wonder] for AI decisions"],
            };
            var nation = new Nation { Name = "Testers", Personality = "Test personality", Cities = ["Capital"] };
            testGame.Ruleset.Nations.Put(nation.Name, nation);
            var civ = testGame.AddCiv(nation);
            var human = testGame.AddCiv(nation, isPlayer: true);

            var melee = testGame.CreateBaseUnit("Melee", "[+10]% weight to this choice for AI decisions");
            melee.Strength = 5;
            var ranged = testGame.CreateBaseUnit("Archery");
            ranged.Strength = 5;
            ranged.RangedStrength = 7;
            var wonder = testGame.CreateWonder();
            var building = testGame.CreateBuilding();
            Assert.Equal(2f * 1.1f, ((IHasUniques)melee).GetWeightForAiDecision(new GameContext(civ)), 4);
            Assert.Equal(1f, ((IHasUniques)ranged).GetWeightForAiDecision(new GameContext(civ)));
            Assert.Equal(0.5f, ((IHasUniques)wonder).GetWeightForAiDecision(new GameContext(civ)));
            Assert.Equal(1f, ((IHasUniques)building).GetWeightForAiDecision(new GameContext(civ)));
            // without a civ in the context, and for a human player (neutral personality), only the AiChoiceWeight uniques count
            Assert.Equal(1.1f, ((IHasUniques)melee).GetWeightForAiDecision(new GameContext()), 4);
            Assert.Equal(1.1f, ((IHasUniques)melee).GetWeightForAiDecision(new GameContext(human)), 4);
        }
    }
}
