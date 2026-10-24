using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Trade;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TradeObject = Unciv.Logic.Trade.Trade;

namespace Unciv.Core.Tests.Logic.Automation;

// T088. There is no Kotlin fixture for the diplomacy AI ("generate golden values"): the expected numbers below are hand-derived from the
// Kotlin source (every case names the Kotlin line/branch it pins). Neutral personality (all 5): scaledFocus == inverseScaledFocus == 1.
public static partial class DiplomacyAiTests
{
    /// <summary>A hand-written <c>TradeEvaluation</c> replacing the real T089 class through GameClientState.TradeEvaluation (see TradeAutomationEvaluation.cs).</summary>
    internal sealed class StandInEvaluation : ITradeAutomationEvaluation
    {
        public bool TradeValid { get; set; } = true;
        public bool TradeAcceptable { get; set; } = true;
        public int Acceptability { get; set; }
        public int PeaceCost { get; set; }
        public Func<TradeOffer, int> BuyCost { get; set; } = _ => 0;
        public Func<TradeOffer, int> SellCost { get; set; } = _ => 0;
        public bool PeaceProposalEnabled { get; set; }

        public bool IsTradeValid(TradeObject trade, CivilizationObject offerer, CivilizationObject tradePartner) => TradeValid;
        public bool IsTradeAcceptable(TradeObject trade, CivilizationObject evaluator, CivilizationObject tradePartner) => TradeAcceptable;
        public int GetTradeAcceptability(TradeObject trade, CivilizationObject evaluator, CivilizationObject tradePartner, bool includeDiplomaticGifts = false) => Acceptability;
        public bool IsPeaceProposalEnabled(CivilizationObject thirdCiv, CivilizationObject civInfo) => PeaceProposalEnabled;
        public int EvaluateBuyCostWithInflation(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, TradeObject trade) => BuyCost(offer);
        public int EvaluateSellCostWithInflation(TradeOffer offer, CivilizationObject civInfo, CivilizationObject tradePartner, TradeObject trade) => SellCost(offer);
        public int EvaluatePeaceCostForThem(CivilizationObject ourCiv, CivilizationObject otherCiv) => PeaceCost;
    }

    /// <summary>Three AI civs on a desert hexagon (radius 12) who have all met: A (-4,0), B (<c>bX</c>,0, default 4) and T (0,6), one city each.</summary>
    internal sealed class World
    {
        public TestGame Game { get; } = new();
        public CivilizationObject A { get; }
        public CivilizationObject B { get; }
        public CivilizationObject T { get; }
        public CityObject CityA { get; }
        public CityObject CityB { get; }
        public CityObject CityT { get; }
        public StandInEvaluation Evaluation { get; } = new();

        public World(bool bIsHuman = false, int bX = 4, bool bIsCityState = false, int tX = 0, int tY = 6, string[]? uniques = null, string[]? aUniques = null)
        {
            A = Game.AddCiv(aUniques ?? uniques);
            B = bIsCityState ? Game.AddCiv(cityStateType: "Cultured") : Game.AddCiv(uniques, isPlayer: bIsHuman);
            T = Game.AddCiv(uniques);
            Game.GameInfo.EngineOptions.VisibleMap = true;
            Game.MakeHexagonalMap(12);
            CityA = Game.AddCity(A, Game.GetTile(-4, 0));
            CityB = Game.AddCity(B, Game.GetTile(bX, 0));
            CityT = Game.AddCity(T, Game.GetTile(tX, tY));
            A.DiplomacyFunctions.MakeCivilizationsMeet(B);
            A.DiplomacyFunctions.MakeCivilizationsMeet(T);
            B.DiplomacyFunctions.MakeCivilizationsMeet(T);
            Game.GameInfo.ClientState.TradeEvaluation = Evaluation;
        }

        public GameView ViewOf(CivilizationObject civ) => new(Game.GameInfo, civ);

        public CivView CivViewOf(CivilizationObject civ) => ViewOf(civ).CivView;

        public DiplomacyManager Diplo(CivilizationObject from, CivilizationObject to) => from.GetDiplomacyManager(to)!;

        /// <summary>Sets what <c>from</c> thinks of <c>to</c> (the opinion is the sum of the diplomatic modifiers).</summary>
        public void SetOpinion(CivilizationObject from, CivilizationObject to, float opinion) =>
            Diplo(from, to).SetModifier(DiplomaticModifiers.GaveUsGifts, opinion);

        /// <summary>The embassy requirement of the ruleset (<c>MeetsEmbassyRequirementFor</c>): a zero-valued SharedEmbassies modifier both ways.</summary>
        public void ShareEmbassies(CivilizationObject a, CivilizationObject b)
        {
            Diplo(a, b).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
            Diplo(b, a).SetModifier(DiplomaticModifiers.SharedEmbassies, 0f);
        }

        /// <summary>Puts <paramref name="count"/> warriors on distinct empty tiles, nearest first around (x, y).</summary>
        public void AddWarriors(CivilizationObject civ, int count, int x, int y)
        {
            var free = Game.GetTile(x, y).GetTilesInDistance(8)
                .Prepend(Game.GetTile(x, y))
                .Distinct().Where(it => it.MilitaryUnit == null && it.GetCity() == null && it.IsLand)
                .Take(count).ToList();
            Assert.Equal(count, free.Count);
            foreach (var tile in free) Game.AddUnit("Warrior", civ, tile);
            civ.ResetMilitaryMightCache();
        }

        /// <summary>
        /// Runs <paramref name="act"/> once per turn number (the state-based random of the rolls depends on it) until <paramref name="done"/>:
        /// the odds are fixed by the Kotlin code (e.g. 40% for an embassy offer), the winning turn is found by search.
        /// </summary>
        public int RunUntil(Action act, Func<bool> done, int maxTurns = 80)
        {
            for (var turn = 1; turn <= maxTurns; turn++)
            {
                Game.GameInfo.Turns = turn;
                act();
                if (done()) return turn;
            }
            throw new Xunit.Sdk.XunitException($"nothing happened within {maxTurns} turns");
        }

        public float Motivation(CivilizationObject civ, CivilizationObject target, float atLeast = 0f) =>
            MotivationToAttackAutomation.HasAtLeastMotivationToAttack(civ, ViewOf(civ).GetForeignCivView(target), atLeast);
    }
}
