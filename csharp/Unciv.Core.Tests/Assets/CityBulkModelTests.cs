using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Core.Tests.Support;
using Unciv.GodotClient.Settings;
using Unciv.Logic.City;
using Unciv.Logic.Files;
using Unciv.Models.StatTypes;
using Unciv.Queries.Cities;
using Unciv.Queries.Units;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Assets;

// C092: bulk city actions planned from the engine's answers, and what the engine does with the commands they make.
[Collection("RulesetCache")]
public sealed class CityBulkModelTests
{
    private static CityPanelResult Panel(string id, bool puppet = false, string focus = "NoFocus", IReadOnlyList<QueueEntry>? queue = null) => new(
        id, id, new TilePos(1, 1), false, puppet, false, false, new CityGrowth(4, 1, 5, 14, 3, 4, null), new CityBorders(3, 20, true), [], [], [],
        "", queue ?? [], [], [], [], true, focus, false, [], null, null, [], [], []);

    private static ConstructionEntry Entry(string name, string category = "Building", string state = "buildable", bool displayed = true, params ConstructionBuyCost[] buy) =>
        new(name, category, state, displayed, 5, 30, 0, [], buy);

    private static BulkCity City(string id, bool puppet = false, string focus = "NoFocus", IReadOnlyList<QueueEntry>? queue = null, params ConstructionEntry[] entries) =>
        new(Panel(id, puppet, focus, queue), new ConstructionListResult(id, entries));

    private static ConstructionBuyCost Gold(int cost, bool allowed = true) => new("Gold", cost, allowed);

    [Fact]
    public void SetFocus_SkipsPuppetsAndCitiesAlreadyOnIt()
    {
        var plan = CityBulkModel.SetFocus([City("A"), City("B", puppet: true), City("C", focus: "ProductionFocus"), City("D", focus: "FoodFocus")], CityFocus.ProductionFocus);
        Assert.Equal(["A", "D"], plan.Commands.Select(it => it.City));
        Assert.All(plan.Commands, it => Assert.Equal((it.City, CityFocus.ProductionFocus), (((SetCityFocus)it.Command!).CityId, ((SetCityFocus)it.Command!).Focus)));
        Assert.Equal(["a puppet city chooses its own citizens", "already on Production"], plan.Skips.Select(it => it.Skipped));
        Assert.Equal(["A", "B", "C", "D"], plan.Steps.Select(it => it.City));
    }

    [Fact]
    public void SetFocus_RefusesFaithWithoutReligionAndAnUndefinedFocus()
    {
        Assert.Empty(CityBulkModel.SetFocus([City("A")], CityFocus.FaithFocus, religionEnabled: false).Commands);
        Assert.Single(CityBulkModel.SetFocus([City("A")], CityFocus.FaithFocus).Commands);
        Assert.Empty(CityBulkModel.SetFocus([City("A")], (CityFocus)999).Commands);
        Assert.Empty(CityBulkModel.SetFocus([], CityFocus.FoodFocus).Steps);
    }

    [Fact]
    public void Queue_FollowsTheEnginesQueueRules()
    {
        var plan = CityBulkModel.Queue(
        [
            City("A", entries: Entry("Monument")),
            City("B", puppet: true, entries: Entry("Monument")),
            City("C", entries: Entry("Monument", state: "enqueued")),
            City("D", entries: Entry("Monument", state: "built")),
            City("E", entries: Entry("Monument", state: "rejected")),
            City("F", entries: Entry("Granary")),
            City("G", entries: Entry("Warrior", "Unit", "enqueued")),
        ], "Monument");
        Assert.Equal(["A"], plan.Commands.Select(it => it.City));
        Assert.Equal("Monument", ((QueueConstruction)plan.Commands[0].Command!).Name);
        Assert.False(((QueueConstruction)plan.Commands[0].Command!).AddToTop);
        Assert.Equal(["a puppet city chooses its own production", "Already in the queue", "Already built", "It cannot be built now", "Monument is not known here", "Monument is not known here"],
            plan.Skips.Select(it => it.Skipped));
        var unit = CityBulkModel.Queue([City("G", entries: Entry("Warrior", "Unit", "enqueued"))], "Warrior");
        Assert.Single(unit.Commands); // units may be queued again
    }

    [Fact]
    public void Queue_ToTopIsNeverAskedOfAPerpetualItem()
    {
        var plan = CityBulkModel.Queue([City("A", entries: [Entry("Monument"), Entry("Gold", "Perpetual")])], "Gold", toTop: true);
        Assert.False(((QueueConstruction)plan.Commands.Single().Command!).AddToTop);
        Assert.True(((QueueConstruction)CityBulkModel.Queue([City("A", entries: Entry("Monument"))], "Monument", toTop: true).Commands.Single().Command!).AddToTop);
    }

    [Fact]
    public void BuyCurrent_SpendsGoldInOrderAndSkipsWhatItCannotPay()
    {
        QueueEntry[] monument = [new("Monument", 3, 40, 0)];
        var plan = CityBulkModel.BuyCurrent(
        [
            City("A", queue: monument, entries: Entry("Monument", buy: Gold(60))),
            City("B", queue: monument, entries: Entry("Monument", buy: Gold(60))),
            City("C", queue: monument, entries: Entry("Monument", buy: Gold(30, allowed: false))),
            City("D", queue: monument, entries: Entry("Monument", buy: [Gold(50), new ConstructionBuyCost("Faith", 20, true)])),
            City("E"),
            City("F", queue: [new QueueEntry("Gold", null, null, 0)], entries: Entry("Gold", "Perpetual")),
        ], 100);
        Assert.Equal(["A", "D"], plan.Commands.Select(it => it.City)); // after A's 60 only 40 gold is left: B cannot pay, D pays in faith
        var d = (PurchaseConstruction)plan.Commands[1].Command!;
        Assert.Equal(Stat.Faith, d.Stat);
        var a = (PurchaseConstruction)plan.Commands[0].Command!;
        Assert.Equal((Stat.Gold, "Monument", 0), (a.Stat, a.Name, a.QueueIndex));
        Assert.Equal(["not enough gold left for 60", "Monument cannot be bought now", "nothing in production", "Gold cannot be bought"], plan.Skips.Select(it => it.Skipped));
    }

    [Fact]
    public void BuyCurrent_PrefersGoldWhenBothCurrenciesAreAllowed()
    {
        var plan = CityBulkModel.BuyCurrent([City("A", queue: [new QueueEntry("Monument", 3, 40, 0)], entries: Entry("Monument", buy: [new ConstructionBuyCost("Faith", 20, true), Gold(50)]))], 50);
        Assert.Equal(Stat.Gold, ((PurchaseConstruction)plan.Commands.Single().Command!).Stat);
        var poor = CityBulkModel.BuyCurrent([City("A", queue: [new QueueEntry("Monument", 3, 40, 0)], entries: Entry("Monument", buy: [new ConstructionBuyCost("Faith", 20, true), Gold(50)]))], 10);
        Assert.Equal(Stat.Faith, ((PurchaseConstruction)poor.Commands.Single().Command!).Stat); // gold is unaffordable by budget but faith is allowed
    }

    [Fact]
    public void ApplyTemplate_QueuesWhatEachCityCanAndNamesWhatItCannot()
    {
        var template = new BuildTemplate { Name = "Start", Items = ["Monument", "Granary", "Monument", "Warrior", "Warrior", "Nope"] };
        var plan = CityBulkModel.ApplyTemplate(
        [
            City("A", entries: [Entry("Monument"), Entry("Granary", state: "enqueued"), Entry("Warrior", "Unit")]),
            City("B", puppet: true, entries: Entry("Monument")),
        ], template);
        Assert.Equal(["A/Monument", "A/Warrior", "A/Warrior"], plan.Commands.Select(it => it.City + "/" + ((QueueConstruction)it.Command!).Name));
        Assert.Equal(["Already in the queue", "already in the template", "Nope is not known here"], plan.Skips.Where(it => it.City == "A").Select(it => it.Skipped));
        Assert.Equal(6, plan.Skips.Count(it => it.City == "B"));
        Assert.Empty(CityBulkModel.ApplyTemplate([], template).Steps);
    }

    [Fact]
    public void QueueChoices_ListsWhatSomeCityCanQueueOnceInTheEnginesOrder()
    {
        var choices = CityBulkModel.QueueChoices(
        [
            City("A", entries: [Entry("Warrior", "Unit"), Entry("Monument"), Entry("Hidden", displayed: false), Entry("Wonder", "Wonder", "rejected")]),
            City("B", entries: [Entry("Monument"), Entry("Granary"), Entry("Gold", "Perpetual")]),
            City("P", puppet: true, entries: Entry("Library")),
        ]);
        Assert.Equal(["Warrior", "Monument", "Granary", "Gold"], choices);
        Assert.Empty(CityBulkModel.QueueChoices([]));
    }

    [Fact]
    public void Templates_AreTrimmedBoundedAndReplacedByName()
    {
        Assert.Null(new BuildTemplate { Name = "  ", Items = ["Monument"] }.Sanitized());
        Assert.Null(new BuildTemplate { Name = "Empty", Items = [" ", ""] }.Sanitized());
        var clean = new BuildTemplate { Name = "  Mine  ", Items = [" Monument ", "", "Granary"] }.Sanitized();
        Assert.Equal("Mine", clean!.Name);
        Assert.Equal(["Monument", "Granary"], clean.Items);
        Assert.Equal(BuildTemplate.MaxNameLength, new BuildTemplate { Name = new string('x', 100), Items = ["A"] }.Sanitized()!.Name.Length);
        Assert.Equal(BuildTemplate.MaxItems, new BuildTemplate { Name = "Long", Items = Enumerable.Range(0, 50).Select(it => "I" + it).ToList() }.Sanitized()!.Items.Count);

        var list = CityBulkModel.WithTemplate([], clean);
        list = CityBulkModel.WithTemplate(list, new BuildTemplate { Name = "Mine", Items = ["Library"] });
        Assert.Equal(["Library"], Assert.Single(list).Items);
        Assert.Equal(list, CityBulkModel.WithTemplate(list, new BuildTemplate { Name = "", Items = ["X"] }));
        for (int i = 0; i < BuildTemplate.MaxTemplates + 5; i++) list = CityBulkModel.WithTemplate(list, new BuildTemplate { Name = "T" + i, Items = ["X"] });
        Assert.Equal(BuildTemplate.MaxTemplates, list.Count);
        Assert.Equal("Library", CityBulkModel.WithTemplate(list, new BuildTemplate { Name = "Mine", Items = ["Library"] })[0].Items[0]); // replacing works at the limit
        Assert.DoesNotContain(CityBulkModel.WithoutTemplate(list, "Mine"), it => it.Name == "Mine");
        Assert.Equal(list.Count, CityBulkModel.WithoutTemplate(list, "Nope").Count);
        Assert.Empty(CityBulkModel.SanitizeTemplates(null));
        Assert.Equal(["A"], CityBulkModel.SanitizeTemplates([new BuildTemplate { Name = "A", Items = ["X"] }, new BuildTemplate { Name = "A", Items = ["Y"] }, null!, new BuildTemplate { Name = "B", Items = [] }]).Select(it => it.Name));
    }

    [Fact]
    public void FromQueue_HoldsTheQueueInOrderAndIsNullWhenEmptyOrUnnamed()
    {
        var city = Panel("A", queue: [new QueueEntry("Monument", 1, 1, 0), new QueueEntry("Gold", null, null, 0)]);
        Assert.Equal(["Monument", "Gold"], CityBulkModel.FromQueue(city, "Q")!.Items);
        Assert.Null(CityBulkModel.FromQueue(Panel("A"), "Q"));
        Assert.Null(CityBulkModel.FromQueue(city, " "));
    }

    [Fact]
    public void Outcome_CountsAndShortensTheProblemList()
    {
        Assert.Equal("1 change made.", new BulkOutcome(1, []).Text());
        Assert.Equal("0 changes made. Not done: a; b; c (and 2 more)", new BulkOutcome(0, ["a", "b", "c", "d", "e"]).Text());
        Assert.Equal("2 changes made. Not done: a", new BulkOutcome(2, ["a"]).Text());
    }

    [Fact]
    public void BuildTemplatesSurviveTheSettingsFileAndAHandEditedOneIsMadeSafe()
    {
        string dir = Directory.CreateTempSubdirectory("unciv-bulk-").FullName;
        try
        {
            var store = new ClientSettingsStore(Path.Combine(dir, "settings.json"));
            var service = new ClientSettingsService(store);
            service.Update(s => s with { BuildTemplates = [new BuildTemplate { Name = "Core", Items = ["Monument", "Granary"] }] });
            var loaded = new ClientSettingsStore(store.Path).Load();
            Assert.Equal("Core", Assert.Single(loaded.BuildTemplates).Name);
            Assert.Equal(["Monument", "Granary"], loaded.BuildTemplates[0].Items);
            File.WriteAllText(store.Path, """{"version":1,"buildTemplates":[{"name":"","items":["A"]},{"name":"Ok","items":[" A ",null]},null]}""");
            Assert.Equal(["Ok"], store.Load().BuildTemplates.Select(it => it.Name));
            Assert.Equal(["A"], store.Load().BuildTemplates[0].Items);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ---- the real engine: the commands a plan makes are ordinary city commands

    private sealed class Env
    {
        public readonly TestGame Game = new();
        public readonly CivilizationObject Civ;
        public readonly CityObject One;
        public readonly CityObject Two;
        public readonly CityObject Puppet;
        public readonly GameSession Session;

        public Env(int gold)
        {
            Game.MakeHexagonalMap(9);
            Civ = Game.AddCiv(isPlayer: true);
            One = Game.AddCity(Civ, Game.GetTile(0, 0), initialPopulation: 3);
            Two = Game.AddCity(Civ, Game.GetTile(5, 0), initialPopulation: 3);
            Puppet = Game.AddCity(Civ, Game.GetTile(-5, 0), initialPopulation: 3);
            Puppet.IsPuppet = true;
            Game.GameInfo.CurrentPlayerCiv = Civ;
            Game.GameInfo.CurrentPlayer = Civ.CivId;
            Civ.AddGold(gold - Civ.Gold);
            One.CityConstructions.RemoveAll();
            Two.CityConstructions.RemoveAll();
            Session = new GameSession(Game.GameInfo);
        }

        public IReadOnlyList<BulkCity> Cities() => new[] { One, Two, Puppet }.Select(it =>
        {
            var view = new Unciv.View.GameView(Game.GameInfo, Civ);
            return new BulkCity(new CityPanelQuery(it.Id).Run(view), new ConstructionListQuery(it.Id, false).Run(view));
        }).ToList();

        public ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(Game.GameInfo, forceZip: false)));

        public int Run(BulkPlan plan) => plan.Commands.Count(it => Session.Submit(Civ.CivId, it.Command!).Accepted);
    }

    [Fact]
    public void RealEngine_FocusAndQueueReachEveryCityThatAllowsThem()
    {
        var env = new Env(0);
        var focus = CityBulkModel.SetFocus(env.Cities(), CityFocus.ProductionFocus);
        Assert.Equal(2, env.Run(focus));
        Assert.Equal([CityFocus.ProductionFocus, CityFocus.ProductionFocus], [env.One.GetCityFocus(), env.Two.GetCityFocus()]);
        Assert.NotEqual(CityFocus.ProductionFocus, env.Puppet.GetCityFocus());

        var queue = CityBulkModel.Queue(env.Cities(), "Monument");
        Assert.Equal(2, queue.Commands.Count);
        Assert.Single(queue.Skips);
        Assert.Equal(2, env.Run(queue));
        Assert.Equal(["Monument"], env.One.CityConstructions.ConstructionQueue);
        Assert.Equal(["Monument"], env.Two.CityConstructions.ConstructionQueue);
        Assert.DoesNotContain("Monument", env.Puppet.CityConstructions.ConstructionQueue);

        var again = CityBulkModel.Queue(env.Cities(), "Monument"); // already queued: the plan skips every city
        Assert.Empty(again.Commands);
    }

    [Fact]
    public void RealEngine_ATemplateAppendsInOrderAndASecondApplicationQueuesNothing()
    {
        var env = new Env(0);
        var template = new BuildTemplate { Name = "T", Items = ["Monument", "Warrior", "Granary"] };
        var plan = CityBulkModel.ApplyTemplate(env.Cities(), template);
        int applied = env.Run(plan);
        Assert.Equal(plan.Commands.Count, applied);
        Assert.Equal(["Monument", "Warrior"], env.One.CityConstructions.ConstructionQueue.Take(2));
        Assert.Equal(env.One.CityConstructions.ConstructionQueue, env.Two.CityConstructions.ConstructionQueue);
        var second = CityBulkModel.ApplyTemplate(env.Cities().Take(2).ToList(), template);
        Assert.DoesNotContain(second.Commands, it => ((QueueConstruction)it.Command!).Name == "Monument");
    }

    [Fact]
    public void RealEngine_BuyingSpendsTheBudgetAndARefusedCommandChangesNothing()
    {
        var env = new Env(0);
        env.Run(CityBulkModel.Queue(env.Cities(), "Monument"));
        var one = env.Cities()[0];
        int price = one.List.Entries.Single(it => it.Name == "Monument").BuyCosts.Single(it => it.Stat == "Gold").Cost;
        env.Civ.AddGold(price + price / 2 - env.Civ.Gold);
        var plan = CityBulkModel.BuyCurrent(env.Cities(), env.Civ.Gold);
        Assert.Single(plan.Commands);
        Assert.Equal("not enough gold left for " + price, plan.Skips.First(it => it.City == env.Two.Name).Skipped);
        Assert.Equal(1, env.Run(plan));
        Assert.Equal(price / 2, env.Civ.Gold);
        Assert.True(env.One.CityConstructions.IsBuilt("Monument"));
        Assert.False(env.Two.CityConstructions.IsBuilt("Monument"));

        // sending the plan for the second city anyway: the engine refuses for lack of gold, state and log stay as they were
        ulong before = env.Hash();
        int logged = env.Session.Log.Count;
        var result = env.Session.Submit(env.Civ.CivId, new PurchaseConstruction { CityId = env.Two.Id, Name = "Monument", QueueIndex = 0 });
        Assert.False(result.Accepted);
        Assert.Equal(before, env.Hash());
        Assert.Equal(logged, env.Session.Log.Count);
    }
}
