using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T087. CityLocationTileRanker.kt has no Kotlin test fixture: C#-only cases, expected values derived by hand from the Kotlin source.
public static partial class AutomationCoreTests
{
    [Collection("RulesetCache")]
    public class CityLocationTileRankerTests
    {
        private readonly TestGame testGame = new();

        private (CivilizationObject Civ, MapUnit Settler) World(bool explored = true, int radius = 12)
        {
            testGame.MakeHexagonalMap(radius);
            foreach (var tile in testGame.TileMap.Values) testGame.SetTileTerrain(tile.Position, Constants.Grassland);
            var civ = testGame.AddCiv(isPlayer: false);
            var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 0));
            if (explored) foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
            return (civ, settler);
        }

        [Fact]
        public void FindsTheBestReachableTileAndRanksEveryCandidateAboveTheMinimum()
        {
            var (_, settler) = World();
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            Assert.NotNull(result.BestTile);
            Assert.True(result.TileRankMap.ContainsKey(result.BestTile));
            Assert.Equal(result.TileRankMap[result.BestTile], result.BestTileRank);
            Assert.Equal(result.TileRankMap.Values.Max(), result.BestTileRank); // nothing blocks the way: the best ranked tile is reachable
            Assert.All(result.TileRankMap.Keys, tile => Assert.True(tile.AerialDistanceTo(settler.GetTile()) <= 5)); // no cities: range = 8 - 0 coerced to 5
        }

        [Fact]
        public void IsDeterministic()
        {
            var (_, settler) = World();
            var first = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            var second = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            Assert.Same(first.BestTile, second.BestTile);
            Assert.Equal(first.TileRankMap.Keys, second.TileRankMap.Keys);
            Assert.Equal(first.TileRankMap.Values, second.TileRankMap.Values);
        }

        [Fact]
        public void MinimumValueFiltersAndYieldsNothingWhenTooHigh()
        {
            var (_, settler) = World();
            var all = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            var cut = all.TileRankMap.Values.Max() - 0.01f;
            var high = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, cut);
            Assert.All(high.TileRankMap.Values, value => Assert.True(value >= cut));
            Assert.True(high.TileRankMap.Count < all.TileRankMap.Count);
            var none = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, 1e9f);
            Assert.Empty(none.TileRankMap);
            Assert.Null(none.BestTile);
            Assert.Equal(0f, none.BestTileRank);
        }

        [Fact]
        public void UnexploredTilesAreNeverCandidates()
        {
            var (civ, settler) = World(explored: false); // only what the settler itself sees is known
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            Assert.NotEmpty(result.TileRankMap);
            Assert.All(result.TileRankMap.Keys, tile => Assert.True(civ.HasExplored(tile)));
            Assert.False(civ.HasExplored(testGame.GetTile(0, 10)));
            foreach (var tile in testGame.TileMap.Values) tile.SetExplored(civ, true);
            Assert.True(result.TileRankMap.Count < CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f).TileRankMap.Count);
        }

        [Fact]
        public void ExplicitSearchDistanceLimitsTheCandidates()
        {
            var (_, settler) = World();
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 1, -1000f);
            Assert.NotEmpty(result.TileRankMap);
            Assert.All(result.TileRankMap.Keys, tile => Assert.True(tile.AerialDistanceTo(settler.GetTile()) <= 1));
        }

        [Fact]
        public void FarFromHomeTheSearchRangeShrinksButStaysAtLeastOne()
        {
            var (civ, settler) = World();
            testGame.AddCity(civ, testGame.GetTile(0, 8)); // home city 8 away: range = 8 - 8 = 0 -> 1
            var tile = testGame.GetTile(0, 0);
            settler.RemoveFromTile();
            settler.PutInTile(tile);
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, null, -1000f);
            Assert.All(result.TileRankMap.Keys, candidate => Assert.True(candidate.AerialDistanceTo(tile) <= 1));
        }

        [Fact]
        public void TilesNearExistingCitiesAreExcludedByTheMinimalCityDistance()
        {
            var (civ, settler) = World();
            var city = testGame.AddCity(civ, testGame.GetTile(0, 4));
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 5, -1000f);
            var minimal = testGame.Ruleset.ModOptions.Constants.MinimalCityDistance;
            Assert.NotEmpty(result.TileRankMap);
            Assert.All(result.TileRankMap.Keys, tile => Assert.True(tile.AerialDistanceTo(city.GetCenterTile()) > minimal));
        }

        [Fact]
        public void FoundingSpotsOfOtherCivsAreExcluded()
        {
            var (_, settler) = World();
            var other = testGame.AddCiv();
            var city = testGame.AddCity(other, testGame.GetTile(0, 5));
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 5, -1000f);
            Assert.DoesNotContain(city.GetCenterTile(), result.TileRankMap.Keys);
            Assert.All(result.TileRankMap.Keys, tile => Assert.True(tile.GetOwner() == null || tile.GetOwner() == settler.Civ));
        }

        [Fact]
        public void AgreedToNotSettleNearUsKeepsTheSettlerOutOfTheOtherCivsSurroundings()
        {
            var (civ, settler) = World();
            var other = testGame.AddCiv();
            var city = testGame.AddCity(other, testGame.GetTile(0, 10));
            civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
            var without = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 5, -1000f);
            Assert.NotEmpty(without.TileRankMap);
            other.GetDiplomacyManager(civ)!.SetFlag(DiplomacyFlags.AgreedToNotSettleNearUs, 10);
            var with = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 5, -1000f);
            Assert.All(with.TileRankMap.Keys, tile => Assert.True(tile.AerialDistanceTo(city.GetCenterTile()) > 6));
            Assert.True(with.TileRankMap.Count < without.TileRankMap.Count);
        }

        [Fact]
        public void WaterAndImpassableTilesCannotBeSettled()
        {
            var (_, settler) = World();
            var water = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Ocean);
            var mountain = testGame.SetTileTerrain(new HexCoord(0, 2), Constants.Mountain);
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 3, -1000f);
            Assert.DoesNotContain(water, result.TileRankMap.Keys);
            Assert.DoesNotContain(mountain, result.TileRankMap.Keys);
        }

        [Fact]
        public void LuxuryHillRiverAndCoastBonusesRaiseTheRank()
        {
            var (civ, settler) = World();
            var candidate = testGame.GetTile(2, 0);
            float Rank() => CityLocationTileRanker.GetBestTilesToFoundCity(settler, 3, -1000f).TileRankMap[candidate];
            var baseRank = Rank();

            var hill = testGame.SetTileTerrainAndFeatures(candidate.Position, Constants.Grassland, Constants.Hill);
            Assert.True(hill.IsHill());
            Assert.True(Rank() > baseRank + 10f); // +14 for hills, minus the changed yields

            testGame.SetTileTerrainAndFeatures(candidate.Position, Constants.Grassland);
            var luxury = testGame.Ruleset.TileResources.Values.First(it => it.ResourceType == Unciv.Models.Ruleset.Tile.ResourceType.Luxury && it.RevealedBy == null);
            var neighbor = testGame.GetTile(3, 0);
            neighbor.TileResource = luxury;
            Assert.False(civ.HasResource(luxury));
            Assert.True(Rank() > baseRank + 5f); // a new luxury resource in the ring is worth +10 (+10 base) at a third of its ring weight at least
        }

        [Fact]
        public void ShowSettlerScoresPublishesTheScoresToTheEngineOptions()
        {
            var (_, settler) = World();
            var options = testGame.GameInfo.EngineOptions;
            Assert.Empty(options.SettlerScores);
            CityLocationTileRanker.GetBestTilesToFoundCity(settler, 2, -1000f);
            Assert.Empty(options.SettlerScores); // off by default
            options.ShowSettlerScores = true;
            var result = CityLocationTileRanker.GetBestTilesToFoundCity(settler, 2, -1000f);
            Assert.Equal(result.TileRankMap.Count, options.SettlerScores.Count);
            foreach (var (tile, value) in result.TileRankMap) Assert.Equal(value, options.SettlerScores[tile.Position]);
        }
    }
}
