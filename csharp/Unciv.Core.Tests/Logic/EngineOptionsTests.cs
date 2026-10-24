using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic;

public class EngineOptionsTests
{
    private static ulong HashOf(EngineOptions options) => StateHasher.ComputeHash(options.HashState);

    [Fact]
    public void DefaultsMatchDebugUtils()
    {
        var options = new EngineOptions();
        Assert.False(options.VisibleMap);
        Assert.False(options.Supercharged);
        Assert.Equal(0, options.SimulateUntilTurn);
        Assert.Empty(options.CivIdsInExperimentGroup);
        Assert.False(options.ShowSettlerScores);
        Assert.Empty(options.SettlerScores);
    }

    [Fact]
    public void IsSimulationIsTurnsBelowTheTarget()
    {
        var options = new EngineOptions { SimulateUntilTurn = 10 };
        Assert.True(options.IsSimulation(9));
        Assert.False(options.IsSimulation(10));
        Assert.False(new EngineOptions().IsSimulation(0));
    }

    [Fact]
    public void EveryRuleAffectingOptionChangesTheStateHash()
    {
        ulong baseline = HashOf(new EngineOptions());
        Assert.Equal(baseline, HashOf(new EngineOptions()));
        var changed = new[]
        {
            HashOf(new EngineOptions { VisibleMap = true }),
            HashOf(new EngineOptions { Supercharged = true }),
            HashOf(new EngineOptions { SimulateUntilTurn = 5 }),
            HashOf(new EngineOptions { CivIdsInExperimentGroup = ["Rome"] }),
        };
        Assert.DoesNotContain(baseline, changed);
        Assert.Equal(changed.Length, changed.Distinct().Count());
    }

    [Fact]
    public void ExperimentGroupHashIgnoresOrderAndDiagnosticsAreNotHashed()
    {
        var a = new EngineOptions { CivIdsInExperimentGroup = ["Rome", "Greece"] };
        var b = new EngineOptions { CivIdsInExperimentGroup = ["Greece", "Rome"] };
        Assert.Equal(HashOf(a), HashOf(b));

        b.ShowSettlerScores = true;
        b.SettlerScores = new Dictionary<HexCoord, float> { [new HexCoord(1, 2)] = 3.5f };
        Assert.Equal(HashOf(a), HashOf(b));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var original = new EngineOptions { VisibleMap = true, SimulateUntilTurn = 7, CivIdsInExperimentGroup = ["Rome"] };
        var clone = original.Clone();
        Assert.Equal(HashOf(original), HashOf(clone));

        clone.CivIdsInExperimentGroup.Add("Greece");
        clone.SimulateUntilTurn = 0;
        Assert.Equal(["Rome"], original.CivIdsInExperimentGroup.ToArray());
        Assert.Equal(7, original.SimulateUntilTurn);
        Assert.NotEqual(HashOf(original), HashOf(clone));
    }
}
