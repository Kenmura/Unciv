using Unciv.Logic;

namespace Unciv.Core.Tests.Logic;

/// <summary>
/// Ported from <c>tests/src/com/unciv/logic/MultiFilterTests.kt</c>.
/// Note: Tests in <c>MultiFilterTests.kt</c> that depend on <c>UniqueParameterType</c> (T025),
/// <c>Unique</c> (T027), <c>Conditionals</c> (T028), <c>TestGame</c>/<c>City</c> (T035), and
/// <c>UniqueValidator</c> (T036) are deferred to those tickets; their underlying <see cref="MultiFilter"/>
/// behavior (<c>forUniqueValidityTests = true</c> and nested <c>non-[{...}]</c> extraction) is tested directly here.
/// </summary>
public class MultiFilterTests
{
    [Fact]
    public void TestSplitTerms()
    {
        Assert.True(MultiFilter.Filter("{A} {B}", it => it == "A" || it == "B"));
        Assert.False(MultiFilter.Filter("{A} {B}", it => it == "A"));
        Assert.False(MultiFilter.Filter("{A} {B}", it => it == "B"));
    }

    [Fact]
    public void TestNotTerm()
    {
        Assert.True(MultiFilter.Filter("non-[B]", it => it == "A"));
        Assert.False(MultiFilter.Filter("non-[A]", it => it == "A"));
    }

    [Fact]
    public void TestSplitNotTerm()
    {
        Assert.True(MultiFilter.Filter("{non-[A]} {non-[B]}", it => it == "C"));
        Assert.False(MultiFilter.Filter("{non-[A]} {non-[B]}", it => it == "A"));
    }

    [Fact]
    public void TestNegatingCombinedFilter()
    {
        // Mirrors "in [non-[{Puppeted} {Resisting}]] cities" from MultiFilterTests.kt
        const string filter = "non-[{Puppeted} {Resisting}]";
        Assert.True(MultiFilter.Filter(filter, _ => false));
        Assert.True(MultiFilter.Filter(filter, it => it == "Puppeted"));
        Assert.True(MultiFilter.Filter(filter, it => it == "Resisting"));
        Assert.False(MultiFilter.Filter(filter, it => it == "Puppeted" || it == "Resisting"));
    }

    [Fact]
    public void TestForUniqueValidityTestsDoesNotInvertNotFilter()
    {
        // When validating parameter types (forUniqueValidityTests: true), non-[X] checks whether X is a valid filter name.
        HashSet<string> validUnitFilters = ["Wounded", "Barbarian", "Melee", "Land"];
        Assert.True(MultiFilter.Filter("non-[Wounded]", validUnitFilters.Contains, forUniqueValidityTests: true));
        Assert.False(MultiFilter.Filter("non-[NONEXISTENT]", validUnitFilters.Contains, forUniqueValidityTests: true));
        Assert.True(MultiFilter.Filter("{non-[Wounded]} {Barbarian}", validUnitFilters.Contains, forUniqueValidityTests: true));
        Assert.False(MultiFilter.Filter("{Wounded} {NONEXISTENT}", validUnitFilters.Contains, forUniqueValidityTests: true));
        Assert.True(MultiFilter.Filter("{Wounded} {Melee} {Land}", validUnitFilters.Contains, forUniqueValidityTests: true));
    }

    [Fact]
    public void TestGetAllSingleFilters()
    {
        const string complex = "non-[{non-[Resisting]} {non-[Razing]} {non-[Coastal]}]";
        string[] singles = MultiFilter.GetAllSingleFilters(complex).ToArray();
        Assert.Equal(["Resisting", "Razing", "Coastal"], singles);

        Assert.Equal(["Simple"], MultiFilter.GetAllSingleFilters("Simple").ToArray());
        Assert.Equal(["Wounded"], MultiFilter.GetAllSingleFilters("non-[Wounded]").ToArray());
    }

    [Fact]
    public void TestStructureHelpers()
    {
        Assert.True(MultiFilter.IsAnd("{A} {B}"));
        Assert.False(MultiFilter.IsAnd("{A}"));
        Assert.False(MultiFilter.IsAnd("A} {B"));
        Assert.Equal(["A", "B"], MultiFilter.GetAndFilters("{A} {B}").ToArray());

        Assert.True(MultiFilter.IsNot("non-[A]"));
        Assert.False(MultiFilter.IsNot("non-A]"));
        Assert.False(MultiFilter.IsNot("non-[A"));
        Assert.Equal("A", MultiFilter.GetNotFilter("non-[A]"));
    }
}
