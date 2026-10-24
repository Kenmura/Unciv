using System.Text.Json;
using Unciv.Json;
using Unciv.Logic.Civilization.Managers;
using Unciv.Utils;

namespace Unciv.Core.Tests.Json;

// T113. Golden strings come from running the real libGDX 1.14.2 Json (OutputType.json, as core/.../json/UncivJson.kt) over a
// class with the same field names and java.util HashSet/HashMap/IntArray fields.
public class JavaHashConvertersTests
{
    private static readonly string[] Techs =
    [
        "Agriculture", "Pottery", "Animal Husbandry", "Archery", "The Wheel", "Mining", "Sailing", "Calendar", "Writing",
        "Masonry", "Bronze Working", "Currency", "Mathematics"
    ];

    private const string GdxTechsResearched =
        """["Calendar","Mathematics","Masonry","Mining","Bronze Working","Sailing","The Wheel","Archery","Currency","Pottery","Animal Husbandry","Writing","Agriculture"]""";

    private const string GdxTechsInProgress =
        """{"Calendar":56,"Mathematics":77,"Masonry":49,"Mining":42,"Bronze Working":98,"Sailing":49,"The Wheel":63,"Archery":49,"Currency":56,"Pottery":49,"Animal Husbandry":112,"Writing":49,"Agriculture":77}""";

    private static HashSet<string> Names(params IEnumerable<string> names) => names.ToHashSet();

    private static TechManager NewTech()
    {
        var tech = new TechManager();
        foreach (var name in Techs)
        {
            tech.TechsResearched.Add(name);
            tech.TechsInProgress.Put(name, name.Length * 7);
        }
        tech.ScienceOfLast8Turns[1] = 3;
        tech.TechsToResearch.Add("Writing");
        return tech;
    }

    [Fact]
    public void TechManagerWritesGdxFieldNamesAndJavaOrder()
    {
        string json = UncivJson.Serialize(NewTech());
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(it => it.Name).ToHashSet();
        Assert.Equal(
            Names("freeTechs", "repeatingTechsResearched", "scienceOfLast8Turns", "scienceFromResearchAgreements", "techsResearched",
                "techsToResearch", "overflowScience", "techsInProgress", "goldPercentConvertedToScience"),
            Names(names));
        Assert.Equal(GdxTechsResearched, doc.RootElement.GetProperty("techsResearched").GetRawText());
        Assert.Equal(GdxTechsInProgress, doc.RootElement.GetProperty("techsInProgress").GetRawText());
        Assert.Equal("[0,3,0,0,0,0,0,0]", doc.RootElement.GetProperty("scienceOfLast8Turns").GetRawText());
    }

    [Fact]
    public void TechManagerReadsFileOrderIntoJavaOrder()
    {
        // The Gdx golden for reading {"techsResearched":["b","a"],"techsInProgress":{"x":1,"y":2},"overflowScience":9}
        // printed "[a, b] {x=1, y=2} 9": insertion order is irrelevant, the java.util table decides.
        var tech = UncivJson.Deserialize<TechManager>("""{"techsResearched":["b","a"],"techsInProgress":{"y":2,"x":1},"overflowScience":9}""")!;
        Assert.Equal(["a", "b"], tech.TechsResearched);
        Assert.Equal(["x", "y"], tech.TechsInProgress.Keys);
        Assert.Equal(2, tech.TechsInProgress["y"]);
        Assert.Contains("\"overflowScience\":9", UncivJson.Serialize(tech));
    }

    [Fact]
    public void TechManagerRoundtrip()
    {
        var copy = UncivJson.Deserialize<TechManager>(UncivJson.Serialize(NewTech()))!;
        Assert.Equal(Techs.OrderBy(it => it, StringComparer.Ordinal), copy.TechsResearched.OrderBy(it => it, StringComparer.Ordinal));
        Assert.Equal(NewTech().TechsResearched, copy.TechsResearched);
        Assert.Equal(NewTech().TechsInProgress, copy.TechsInProgress);
        Assert.Equal(3, copy.ScienceOfLast8Turns[1]);
        Assert.Equal(["Writing"], copy.TechsToResearch);
        Assert.Equal(0.6f, copy.GoldPercentConvertedToScience);
    }

    [Fact]
    public void PolicyManagerIncludesPrivateAndInternalFields()
    {
        var policies = new PolicyManager { FreePolicies = 2, StoredCulture = 30, ShouldOpenPolicyPicker = true };
        policies.AdoptedPolicies.Add("Tradition");
        policies.AdoptedPolicies.Add("Liberty");
        string json = UncivJson.Serialize(policies);
        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(
                Names("freePolicies", "storedCulture", "adoptedPolicies", "numberOfAdoptedPolicies", "cultureOfLast8Turns", "shouldOpenPolicyPicker"),
                Names(doc.RootElement.EnumerateObject().Select(it => it.Name)));
        }
        var copy = UncivJson.Deserialize<PolicyManager>(
            """{"freePolicies":2,"storedCulture":30,"adoptedPolicies":["Tradition","Liberty"],"numberOfAdoptedPolicies":2,"cultureOfLast8Turns":[1,2,3,4,5,6,7,8],"shouldOpenPolicyPicker":true}""")!;
        Assert.Equal(policies.AdoptedPolicies, copy.AdoptedPolicies);
        Assert.Equal(2, copy.FreePolicies);
        Assert.True(copy.ShouldOpenPolicyPicker);
        string again = UncivJson.Serialize(copy);
        Assert.Contains("\"numberOfAdoptedPolicies\":2", again);
        Assert.Contains("\"cultureOfLast8Turns\":[1,2,3,4,5,6,7,8]", again);
    }

    [Fact]
    public void GoldenAgeManagerIncludesPrivateCounter()
    {
        var copy = UncivJson.Deserialize<GoldenAgeManager>("""{"storedHappiness":5,"numberOfGoldenAges":3,"turnsLeftForCurrentGoldenAge":7}""")!;
        Assert.Equal(5, copy.StoredHappiness);
        Assert.Equal(7, copy.TurnsLeftForCurrentGoldenAge);
        using var doc = JsonDocument.Parse(UncivJson.Serialize(copy)); // member order differs from Gdx (fields after properties), irrelevant to readers
        Assert.Equal(3, doc.RootElement.GetProperty("numberOfGoldenAges").GetInt32());
    }

    [Fact]
    public void GreatPersonManagerRoundtrip()
    {
        var gp = new GreatPersonManager { FreeGreatPeople = 1, MayaLimitedFreeGP = 2, GreatGeneralPoints = 4 };
        gp.LongCountGPPool.Add("Great Scientist");
        gp.LongCountGPPool.Add("Great Merchant");
        gp.GreatPersonPointsCounter["Great Engineer"] = 12;
        string json = UncivJson.Serialize(gp);
        using (var doc = JsonDocument.Parse(json))
        {
            Assert.Equal(
                Names("pointsForNextGreatPersonCounter", "pointsForNextGreatGeneral", "pointsForNextGreatGeneralCounter", "greatPersonPointsCounter",
                    "greatGeneralPointsCounter", "greatGeneralPoints", "freeGreatPeople", "mayaLimitedFreeGP", "longCountGPPool"),
                Names(doc.RootElement.EnumerateObject().Select(it => it.Name)));
        }
        var copy = UncivJson.Deserialize<GreatPersonManager>(json)!;
        Assert.Equal(gp.LongCountGPPool, copy.LongCountGPPool);
        Assert.Equal(12, copy.GreatPersonPointsCounter["Great Engineer"]);
        Assert.Equal(2, copy.MayaLimitedFreeGP);
        Assert.Equal(json, UncivJson.Serialize(copy));
    }

    [Fact]
    public void HashMapReadsVerboseAndStringValuesAndRejectsNonStringKeys()
    {
        var map = UncivJson.Deserialize<JavaHashMap<string, int>>("""{"a":"4","b":{"class":"java.lang.Float","value":2.9}}""")!;
        Assert.Equal(4, map["a"]);
        Assert.Equal(2, map["b"]);
        Assert.Throws<NotSupportedException>(() => UncivJson.Serialize(new JavaHashMap<int, int>()));
        Assert.Throws<JsonException>(() => UncivJson.Deserialize<JavaHashSet<string>>("""{"a":1}"""));
    }
}
