using Unciv.Models;

namespace Unciv.Core.Tests.Models;

// tests/src/com/unciv/logic/ModConstantsTests.kt
public class ModConstantsTests
{
    [Fact]
    public void HashCodeOfDefaultsEqualsFreshInstance()
    {
        Assert.Equal(ModConstants.Defaults.GetHashCode(), new ModConstants().GetHashCode());
    }

    [Fact]
    public void HashCodesMatchTheJvm()
    {
        // Golden from the real class on the JVM
        Assert.Equal(670061036, ModConstants.Defaults.GetHashCode());
        var changed = new ModConstants { MaxXPfromBarbarians = 99, CityStrengthBase = 6.0 };
        Assert.Equal(1000388743, changed.GetHashCode());
    }

    [Fact]
    public void Equality()
    {
        var instance1 = new ModConstants { MaxXPfromBarbarians = 99 };
        var instance2 = new ModConstants { CityStrengthBase = 6.0 };
        Assert.NotEqual(instance1, instance2);
        instance1.CityStrengthBase = instance2.CityStrengthBase;
        instance2.MaxXPfromBarbarians = instance1.MaxXPfromBarbarians;
        Assert.Equal(instance1, instance2);
    }

    [Fact]
    public void EqualityFollowsJavaBoxedSemantics()
    {
        Assert.Equal(new ModConstants { CityStrengthBase = double.NaN }, new ModConstants { CityStrengthBase = double.NaN });
        Assert.NotEqual(new ModConstants { CityStrengthBase = 0.0 }, new ModConstants { CityStrengthBase = -0.0 });
    }

    [Fact]
    public void ToStringListsOnlyChangedFields()
    {
        var instance = new ModConstants { MaxXPfromBarbarians = 99, CityStrengthBase = 6.0 };
        Assert.Equal("{maxXPfromBarbarians:99,cityStrengthBase:6.0}", instance.ToString());
        Assert.Equal("defaults", ModConstants.Defaults.ToString());
    }

    [Fact]
    public void MergeCopiesOnlyNonDefaultValues()
    {
        var instance1 = new ModConstants { MaxXPfromBarbarians = 99 };
        var instance2 = new ModConstants { CityStrengthBase = 6.0 };
        instance1.Merge(instance2);
        Assert.Equal(6.0, instance1.CityStrengthBase);
        Assert.Equal(99, instance1.MaxXPfromBarbarians);
    }

    [Fact]
    public void UnitUpgradeCostPrintsLikeKotlinDataClass()
    {
        Assert.Equal("UnitUpgradeCost(base=10.0, perProduction=2.0, eraMultiplier=0.0, exponent=1.0, roundTo=5)", new ModConstants().UnitUpgradeCost.ToString());
        Assert.Equal(new ModConstants().UnitUpgradeCost, new ModConstants.UpgradeCostConstants());
    }

    [Fact]
    public void JsonUsesKotlinNames()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ModConstants());
        Assert.Contains("\"maxXPfromBarbarians\":30", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ModConstants>("{\"maxXPfromBarbarians\":7,\"unitUpgradeCost\":{\"roundTo\":1}}")!;
        Assert.Equal(7, back.MaxXPfromBarbarians);
        Assert.Equal(1, back.UnitUpgradeCost.RoundTo);
    }
}

public class UncivSoundAndTutorialTests
{
    [Fact]
    public void SilentIsEmptyAndRecordsCompareByName()
    {
        Assert.Equal("", UncivSound.Silent.FileName);
        Assert.Equal(new UncivSound("click"), UncivSound.Click);
    }

    [Fact]
    public void TutorialValuesAndCivilopediaFlag()
    {
        Assert.Equal(41, Enum.GetValues<TutorialTrigger>().Length);
        Assert.All(Enum.GetValues<TutorialTrigger>(), t => Assert.Equal(!t.Value().StartsWith('_'), t.IsCivilopedia()));
        Assert.Equal(Enum.GetValues<TutorialTrigger>().Length, Enum.GetValues<TutorialTrigger>().Select(t => t.Value()).Distinct().Count());
    }
}
