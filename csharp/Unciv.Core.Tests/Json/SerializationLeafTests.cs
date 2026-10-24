using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.UI.Screens.OverviewScreen;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.Core.Tests.Json;

// Port of tests/src/com/unciv/logic/SerializationTests.kt: LastSeenImprovement, CivRankingHistory and Notification
public class SerializationLeafTests
{
    private static T Roundtrip<T>(T data)
    {
        string serialized = UncivJson.Serialize(data);
        return UncivJson.Deserialize<T>(serialized)!;
    }

    [Fact]
    public void TestLastSeenImprovementSerializationRoundtrip()
    {
        var data = new LastSeenImprovement
        {
            [HexCoord.Zero] = "Borehole",
            [HexCoord.Of(1, 0)] = "Smokestack",
            [HexCoord.Of(0, 1)] = "Waffle stand"
        };
        var copy = Roundtrip(data);
        Assert.Equal(data, copy);
        Assert.Equal(data.GetHashCode(), copy.GetHashCode());
        Assert.Equal(// java.util.HashMap iteration order (HexCoord hashes 0, 1, 31)
        """{"(0,0)":"Borehole","(0,1)":"Waffle stand","(1,0)":"Smokestack"}""", UncivJson.Serialize(data));
    }

    [Fact]
    public void TestLastSeenImprovementReadsVerboseAndFloatKeys()
    {
        var data = UncivJson.Deserialize<LastSeenImprovement>("""{"(2.0,-3)":{"class":"java.lang.String","value":"Farm"},"( 4 , 5 )":"Mine"}""")!;
        Assert.Equal("Farm", data[HexCoord.Of(2, -3)]);
        Assert.Equal("Mine", data[HexCoord.Of(4, 5)]);
        Assert.Equal(2, data.Count);
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<LastSeenImprovement>("""{"(1,2)":{"x":"Farm"}}"""));
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<LastSeenImprovement>("""{"1":"Farm"}"""));
    }

    [Fact]
    public void TestLastSeenImprovementEmpty()
    {
        Assert.Equal("{}", UncivJson.Serialize(new LastSeenImprovement()));
        Assert.Empty(Roundtrip(new LastSeenImprovement()));
    }

    [Fact]
    public void TestCivRankingHistorySerializationRoundtrip()
    {
        var data = new CivRankingHistory
        {
            [0] = new Dictionary<RankingType, int> { [RankingType.Force] = 0, [RankingType.Territory] = 7 },
            [20] = new Dictionary<RankingType, int> { [RankingType.Culture] = 42, [RankingType.Territory] = 666 }
        };
        var copy = Roundtrip(data);
        Assert.Equal(data, copy);
        Assert.Equal("""{"0":"F0T7","20":"A42T666"}""", UncivJson.Serialize(data));
        var shallow = data.Clone();
        Assert.Equal(data, shallow);
        Assert.Same(data[0], shallow[0]);
    }

    [Fact]
    public void TestCivRankingHistoryNegativeValuesAndOldFormat()
    {
        var negative = new CivRankingHistory { [3] = new Dictionary<RankingType, int> { [RankingType.Gold] = -12, [RankingType.Score] = 5 } };
        Assert.Equal(negative, Roundtrip(negative));

        var old = UncivJson.Deserialize<CivRankingHistory>("""{"0":{"S":50,"G":120,"Q":1,"SS":2},"1":"S55G80X9Z"}""")!;
        Assert.Equal(50, old[0][RankingType.Score]);
        Assert.Equal(120, old[0][RankingType.Gold]);
        Assert.Equal(2, old[0].Count);
        Assert.Equal(55, old[1][RankingType.Score]);
        Assert.Equal(80, old[1][RankingType.Gold]);
        Assert.Equal(2, old[1].Count);
    }

    [Fact]
    public void TestCivRankingHistoryHostileInput()
    {
        var history = UncivJson.Deserialize<CivRankingHistory>("""{"1":"","2":"S","3":"99999999999S5","4":"S2147483648C7","5":7}""")!;
        Assert.Empty(history[1]);
        Assert.Empty(history[2]);
        Assert.Single(history[3]);
        Assert.Equal(5, history[3][RankingType.Score]);
        Assert.Equal(7, history[4][RankingType.Growth]);
        Assert.False(history[4].ContainsKey(RankingType.Score));
        Assert.Empty(history[5]);
        Assert.Throws<FormatException>(() => UncivJson.Deserialize<CivRankingHistory>("""{"x":"S1"}"""));
    }

    private static List<Notification> TestNotifications() =>
    [
        new("hello", [], [], Notification.NotificationCategory.Espionage),
        new("Oh my goddesses", ["ReligionIcons/Pray"], [new CivilopediaAction("Tutorial/Religion")], Notification.NotificationCategory.Religion),
        new("There's Horses", ["ResourceIcons/Horses"], LocationAction.Of(HexCoord.Zero, HexCoord.Of(1, 0)), Notification.NotificationCategory.General),
        new("An evil overlord has arisen", ["PersonalityIcons/Devil"], [new DiplomacyAction("Russia")], Notification.NotificationCategory.War),
        new("Here's a Wizzard", ["EmojiIcons/Great Scientist"], [new MapUnitAction(HexCoord.Of(0, 1), 42)], Notification.NotificationCategory.Units),
    ];

    // Neither Notification nor NotificationAction support equality contract: compare the serialized form
    private static void AssertEqual(Notification expected, Notification actual)
    {
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Category, actual.Category);
        Assert.Equal(expected.Icons, actual.Icons);
        Assert.Equal(expected.Actions.Count, actual.Actions.Count);
        for (int i = 0; i < expected.Actions.Count; i++)
        {
            Assert.Equal(expected.Actions[i].GetType(), actual.Actions[i].GetType());
            Assert.Equal(NotificationActionsSerializer.ToJson(expected.Actions[i]), NotificationActionsSerializer.ToJson(actual.Actions[i]));
        }
    }

    [Fact]
    public void TestNotificationSerializationRoundtrip()
    {
        var data = TestNotifications();
        var copy = Roundtrip(data);
        Assert.Equal(data.Count, copy.Count);
        for (int i = 0; i < data.Count; i++) AssertEqual(data[i], copy[i]);
    }

    [Fact]
    public void TestNotificationJsonFormat()
    {
        var data = TestNotifications();
        Assert.Equal("""{"category":"Espionage","text":"hello"}""", UncivJson.Serialize(data[0]));
        Assert.Equal(
            """{"text":"There\u0027s Horses","icons":["ResourceIcons/Horses"],"actions":[{"LocationAction":{}},{"LocationAction":{"location":{"x":1}}}]}""",
            UncivJson.Serialize(data[2]));
        Assert.Equal("""{"DiplomacyAction":{"otherCivName":"Russia"}}""", NotificationActionsSerializer.ToJson(new DiplomacyAction("Russia")));
        Assert.Equal("""{"MapUnitAction":{"location":{"y":1},"id":42}}""", NotificationActionsSerializer.ToJson(new MapUnitAction(HexCoord.Of(0, 1), 42)));
    }

    [Fact]
    public void TestAllNotificationActionsRoundtrip()
    {
        NotificationAction[] actions =
        [
            new LocationAction(HexCoord.Of(9, 3)), new TechAction("Pottery"), new CityAction(HexCoord.Of(-9, 3)),
            new DiplomacyAction("France", true), new MayaLongCountAction(), new MapUnitAction(HexCoord.Of(1, 2)),
            new CivilopediaAction("Wonder/Nobel Foundation"), new PromoteUnitAction("Warrior", HexCoord.Of(4, 4), 7),
            new PromoteUnitAction("Archer", HexCoord.Of(0, 5), Constants.NoId),
            new OverviewAction(EmpireOverviewCategories.Wonders, "Pyramids"), new OverviewAction(),
            new PolicyAction("Tradition"), new PolicyAction(), new EspionageAction(), new LinkAction("https://example.com"),
            new ReligionAction("Pantheon"), new ReligionAction(), new ReligionAction("")
        ];
        var data = new Notification("all", ["a", "b"], actions, Notification.NotificationCategory.Cities);
        var copy = Roundtrip(data);
        AssertEqual(data, copy);
        Assert.Equal(actions.Length, copy.Actions.Count);
    }

    [Fact]
    public void TestNotificationActionFactoriesAndDefaults()
    {
        Assert.Equal(2, LocationAction.Of(HexCoord.Zero, null, HexCoord.Of(1, 1)).Count());
        Assert.Empty(LocationAction.Of(new HexCoord?[] { null }));
        var espionage = EspionageAction.WithLocation(HexCoord.Of(2, 2)).ToList();
        Assert.IsType<LocationAction>(espionage[0]);
        Assert.IsType<EspionageAction>(espionage[1]);
        Assert.Single(EspionageAction.WithLocation(null));
        var religion = ReligionAction.WithLocation(HexCoord.Zero, "Islam").ToList();
        Assert.Equal(2, religion.Count);
        Assert.Equal("""{"ReligionAction":{"religionName":"Islam"}}""", NotificationActionsSerializer.ToJson(religion[1]));
    }

    [Fact]
    public void TestNotificationLenientAndHostileReading()
    {
        var read = UncivJson.Deserialize<Notification>("""
            {"text":"x","bogus":1,"actions":[{"TechAction":{"techName":"Writing","unknown":3}},{"Nothing":{}},{"PromoteUnitAction":{"id":"5"}}]}
            """)!;
        Assert.Equal(Notification.NotificationCategory.General, read.Category);
        Assert.Equal(2, read.Actions.Count);
        Assert.Equal("""{"PromoteUnitAction":{"id":5}}""", NotificationActionsSerializer.ToJson(read.Actions[1]));
        Assert.Empty(UncivJson.Deserialize<Notification>("{}")!.Actions);
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<Notification>("""{"category":"Nope"}"""));
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<Notification>("""{"actions":[5]}"""));
        Assert.Throws<System.Text.Json.JsonException>(() => UncivJson.Deserialize<Notification>("""{"actions":[{"OverviewAction":{"page":"Nope"}}]}"""));
        Assert.Equal(Notification.NotificationCategory.War, Notification.SafeValueOf("War"));
        Assert.Null(Notification.SafeValueOf("war"));
    }
}
