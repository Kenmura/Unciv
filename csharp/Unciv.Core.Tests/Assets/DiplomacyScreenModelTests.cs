using Unciv.Assets;
using Unciv.Commands.Diplomacy;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Core.Tests.Assets;

public class DiplomacyScreenModelTests
{
    private static CivDto Civ(string id, bool cityState = false, bool barbarian = false, bool defeated = false, string? name = null) =>
        new() { CivId = id, Name = name ?? id, IsCityState = cityState, IsMajor = !cityState && !barbarian, IsBarbarian = barbarian, IsDefeated = defeated };

    private static DiplomacyDto Rel(string a, string b, bool war = false, string? status = "Peace") => new() { A = a, B = b, AtWar = war, Status = status };

    private static ClientSnapshot Snapshot(IEnumerable<CivDto> civs, IEnumerable<DiplomacyDto> relations) => new()
    {
        Seq = 1,
        Turn = 1,
        Phase = "Playing",
        TurnStatus = new TurnStatusDto { Ended = [], Phase = "Playing" },
        You = "Me",
        Civs = civs.ToList(),
        Tiles = [],
        Cities = [],
        Units = [],
        Diplomacy = relations.ToList(),
        PendingTrades = [],
        Notifications = [],
    };

    private static DiplomacyPanelResult Panel(string status = "Peace", bool cityState = false, float? influence = null, bool ally = false, int treaty = 0, string relationship = "Neutral",
        bool openOurs = false, bool openTheirs = false, int offersFromThem = 0, bool offerFromUs = false, IReadOnlyList<DiplomacyValue>? modifiers = null, IReadOnlyList<DiplomacyValue>? flags = null) =>
        new("Rome", cityState, false, status, relationship, modifiers ?? [], flags ?? [], influence, ally, treaty, true, openOurs, openTheirs, false, false, false, false, offerFromUs, offersFromThem);

    private static DiplomacyActionsResult Actions(bool war = false, bool denounce = false, bool peace = false, bool negotiate = false, bool pledge = false, bool withdraw = false,
        bool gold = false, bool worker = false, params int[] gifts) => new("Rome", war, denounce, peace, negotiate, pledge, withdraw, gold, worker, gifts);

    private static DiplomacyRow RomeRow(bool cityState = false, string status = "Peace") => new("Rome", "Rome", cityState, status);

    // ---- the list

    [Fact]
    public void RowsAreOnlyTheCivsTheViewerHasARelationshipWith()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("Rome"), Civ("Egypt"), Civ("Zulu")], [Rel("Me", "Rome"), Rel("Egypt", "Zulu", war: true, status: "War")]);
        var rows = DiplomacyScreenModel.Rows(snapshot);
        Assert.Equal(["Rome"], rows.Select(it => it.CivId)); // Egypt and Zulu are known only as a war between third parties; Zulu/Egypt appear in Civs but are not met
    }

    [Fact]
    public void RowsDropTheViewerBarbariansAndTheDefeated()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("Barbarians", barbarian: true), Civ("Dead", defeated: true), Civ("Rome")],
            [Rel("Me", "Barbarians", war: true, status: "War"), Rel("Me", "Dead"), Rel("Me", "Rome"), Rel("Me", "Me")]);
        Assert.Equal(["Rome"], DiplomacyScreenModel.Rows(snapshot).Select(it => it.CivId));
    }

    [Fact]
    public void RowsListMajorCivsFirstThenCityStatesEachByName()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("Zulu"), Civ("Aksum", cityState: true), Civ("Egypt"), Civ("Berlin", cityState: true)],
            [Rel("Me", "Zulu"), Rel("Aksum", "Me"), Rel("Me", "Egypt"), Rel("Me", "Berlin")]);
        var rows = DiplomacyScreenModel.Rows(snapshot);
        Assert.Equal(["Egypt", "Zulu", "Aksum", "Berlin"], rows.Select(it => it.CivId));
        Assert.Equal([false, false, true, true], rows.Select(it => it.IsCityState));
    }

    [Fact]
    public void RowsDrawAsFlagNameAndStatusLine()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("Zulu"), Civ("Aksum", cityState: true)], [Rel("Me", "Zulu", war: true, status: "War"), Rel("Me", "Aksum")]);
        var rows = DiplomacyScreenModel.Rows(snapshot);
        Assert.Equal(AssetKey.Of("icon/nation", "Aksum"), rows[1].Icon);
        Assert.Equal(["Peace", "War"], new[] { rows[0].Subtitle, rows[1].Subtitle }.Select(it => it.Replace("City-state - ", "", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.True(rows.Single(it => it.CivId == "Zulu").AtWar);
        Assert.StartsWith("City-state - ", rows.Single(it => it.IsCityState).Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyConfirmedStepsAreDangerButtons()
    {
        var buttons = DiplomacyScreenModel.Buttons(RomeRow(), Panel(), Actions(war: true, denounce: true));
        Assert.All(buttons, it => Assert.Equal(it.NeedsConfirm, it.Danger));
        Assert.Contains(buttons, it => it.Danger);
    }

    [Fact]
    public void RowStatusIsTheSnapshotsOrDerivedFromWar()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("A"), Civ("B"), Civ("C"), Civ("D")],
            [Rel("Me", "A", status: "Protector"), Rel("Me", "B", war: true, status: null), Rel("Me", "C", war: false, status: null), Rel("Me", "D", war: true, status: "DefensivePact")]);
        var rows = DiplomacyScreenModel.Rows(snapshot).ToDictionary(it => it.CivId);
        Assert.Equal("Protector", rows["A"].Status);
        Assert.Equal("War", rows["B"].Status);
        Assert.Equal("Peace", rows["C"].Status);
        Assert.Equal("DefensivePact", rows["D"].Status);
    }

    [Fact]
    public void ARelationshipWithACivMissingFromTheCivListIsSkippedAndDuplicatesCollapse()
    {
        var snapshot = Snapshot([Civ("Me"), Civ("Rome")], [Rel("Me", "Ghost"), Rel("Me", "Rome"), Rel("Rome", "Me", war: true, status: "War")]);
        var rows = DiplomacyScreenModel.Rows(snapshot);
        var row = Assert.Single(rows);
        Assert.Equal("Peace", row.Status); // the first relationship wins
    }

    [Fact]
    public void RowLabelNamesCityStatesAndStatus()
    {
        Assert.Equal("Rome - War", new DiplomacyRow("Rome", "Rome", false, "War").Label);
        Assert.Equal("Kyiv (city-state) - Peace", new DiplomacyRow("Kyiv", "Kyiv", true, "Peace").Label);
    }

    // ---- text

    [Fact]
    public void TitleMarksCityStates() => Assert.Equal(["Rome", "Rome (city-state)"], new[] { Panel(), Panel(cityState: true) }.Select(DiplomacyScreenModel.Title));

    [Fact]
    public void InfoLinesForAMajorCiv()
    {
        Assert.Equal(["Status: Peace", "Attitude: Friend"], DiplomacyScreenModel.InfoLines(Panel(relationship: "Friend")));
        var busy = DiplomacyScreenModel.InfoLines(Panel(treaty: 7, openOurs: true, openTheirs: true, offersFromThem: 2, offerFromUs: true));
        Assert.Equal(["Status: Peace", "Attitude: Neutral", "Peace treaty: war is forbidden for 7 more turns", "Your units may cross their borders",
            "Their units may cross your borders", "They sent you 2 trade offer(s)", "Your offer waits for their answer"], busy);
    }

    [Fact]
    public void InfoLinesForACityStateShowInfluenceAndAlly()
    {
        Assert.Equal(["Status: Peace", "Influence: 35.5 (Friend)"], DiplomacyScreenModel.InfoLines(Panel(cityState: true, influence: 35.5f, relationship: "Friend")));
        Assert.Equal(["Status: Peace", "Influence: 70 (Ally)", "You are its ally"], DiplomacyScreenModel.InfoLines(Panel(cityState: true, influence: 70f, ally: true, relationship: "Ally")));
        Assert.Equal(["Status: Peace", "Influence: 0 (Neutral)"], DiplomacyScreenModel.InfoLines(Panel(cityState: true, influence: null)));
    }

    [Fact]
    public void ModifierLinesSignTheOpinionAndListFlagTurns()
    {
        var lines = DiplomacyScreenModel.ModifierLines(Panel(modifiers: [new("DeclaredWarOnUs", -10f), new("Trade", 2.5f), new("Zero", 0f)], flags: [new("DeclinedPeace", 5f)]));
        Assert.Equal(["DeclaredWarOnUs: -10", "Trade: +2.5", "Zero: +0", "DeclinedPeace: 5 turns"], lines);
        Assert.Empty(DiplomacyScreenModel.ModifierLines(Panel()));
    }

    [Fact]
    public void QuestLinesDescribeEachQuest()
    {
        Assert.Equal(["No quests"], DiplomacyScreenModel.QuestLines(new CityStateQuestsResult("Kyiv", [])));
        var lines = DiplomacyScreenModel.QuestLines(new CityStateQuestsResult("Kyiv",
        [
            new QuestEntry("Route", "Connect your capital", false, 12, 40f, ""),
            new QuestEntry("Contest", "Most wonders", true, null, 60.5f, "You lead"),
        ]));
        Assert.Equal(["Route: Connect your capital (+40 influence, 12 turns left)", "Contest: Most wonders (+60.5 influence, all civs)", "  You lead"], lines);
    }

    // ---- buttons

    private static DiplomacyButton Button(IReadOnlyList<DiplomacyButton> buttons, string id) => buttons.Single(it => it.Id == id);

    [Fact]
    public void MajorCivAtPeaceOffersWarAndDenounceEnabledByTheEngineAlone()
    {
        var on = DiplomacyScreenModel.Buttons(RomeRow(), Panel(), Actions(war: true, denounce: true));
        Assert.Equal(["DeclareWar", "Denounce"], on.Select(it => it.Id));
        Assert.All(on, it => Assert.True(it.Enabled));
        var declare = Assert.IsType<DeclareWar>(Button(on, "DeclareWar").Command);
        Assert.Equal("Rome", declare.CivId);
        Assert.Equal("Rome", Assert.IsType<Denounce>(Button(on, "Denounce").Command).CivId);

        var off = DiplomacyScreenModel.Buttons(RomeRow(), Panel(), Actions(war: false, denounce: true));
        Assert.False(Button(off, "DeclareWar").Enabled);
        Assert.True(Button(off, "Denounce").Enabled);
        var off2 = DiplomacyScreenModel.Buttons(RomeRow(), Panel(), Actions(war: true, denounce: false));
        Assert.True(Button(off2, "DeclareWar").Enabled);
        Assert.False(Button(off2, "Denounce").Enabled);
    }

    [Fact]
    public void WarAndDenounceNeedConfirmationAndPeaceDoesNot()
    {
        var peace = DiplomacyScreenModel.Buttons(RomeRow(), Panel(), Actions(war: true, denounce: true));
        Assert.True(Button(peace, "DeclareWar").NeedsConfirm);
        Assert.True(Button(peace, "Denounce").NeedsConfirm);
        Assert.Equal("Confirm: Declare war", Button(peace, "DeclareWar").ConfirmLabel);
        var atWar = DiplomacyScreenModel.Buttons(RomeRow(status: "War"), Panel("War"), Actions(peace: true));
        Assert.False(Button(atWar, "OfferPeace").NeedsConfirm);
    }

    [Fact]
    public void AtWarAMajorCivOffersOnlyPeaceWhichIsTheEnginesProposal()
    {
        var buttons = DiplomacyScreenModel.Buttons(RomeRow(status: "War"), Panel("War"), Actions(peace: true));
        var peace = Assert.Single(buttons);
        Assert.Equal("OfferPeace", peace.Id);
        Assert.True(peace.Enabled);
        var command = Assert.IsType<ProposeTrade>(peace.Command);
        Assert.Equal("Rome", command.CivId);
        var offer = Assert.Single(command.OurOffers);
        Assert.Equal("Treaty", offer.Kind);
        Assert.Equal(Constants.PeaceTreaty, offer.Name);
        Assert.Empty(command.TheirOffers);
        Assert.False(Assert.Single(DiplomacyScreenModel.Buttons(RomeRow(status: "War"), Panel("War"), Actions(peace: false))).Enabled);
    }

    [Fact]
    public void CityStateAtPeaceOffersGiftsBullyingAndProtection()
    {
        var row = RomeRow(cityState: true);
        var buttons = DiplomacyScreenModel.Buttons(row, Panel(cityState: true), Actions(war: true, pledge: true, gold: true, worker: false, gifts: [250, 1000]));
        Assert.Equal(["Gift_250", "Gift_500", "Gift_1000", "TributeGold", "TributeWorker", "PledgeProtection", "DeclareWar"], buttons.Select(it => it.Id));
        Assert.Equal([true, false, true], new[] { "Gift_250", "Gift_500", "Gift_1000" }.Select(it => Button(buttons, it).Enabled));
        Assert.Equal([250, 500, 1000], new[] { "Gift_250", "Gift_500", "Gift_1000" }.Select(it => Assert.IsType<GiftGold>(Button(buttons, it).Command).Amount));
        Assert.Equal("Gift 500 gold", Button(buttons, "Gift_500").Label);
        Assert.True(Button(buttons, "TributeGold").Enabled);
        Assert.False(Button(buttons, "TributeWorker").Enabled);
        Assert.IsType<TributeGold>(Button(buttons, "TributeGold").Command);
        Assert.IsType<TributeWorker>(Button(buttons, "TributeWorker").Command);
        Assert.True(Button(buttons, "PledgeProtection").Enabled);
        Assert.IsType<PledgeProtection>(Button(buttons, "PledgeProtection").Command);
        Assert.All(buttons, it => Assert.Equal("Rome", it.Command switch { GiftGold g => g.CivId, TributeGold t => t.CivId, TributeWorker w => w.CivId, PledgeProtection p => p.CivId, DeclareWar d => d.CivId, _ => "?" }));
        Assert.DoesNotContain(buttons, it => it.Id == "Denounce");
        Assert.False(Button(DiplomacyScreenModel.Buttons(row, Panel(cityState: true), Actions(war: false)), "PledgeProtection").Enabled);
        Assert.False(Button(DiplomacyScreenModel.Buttons(row, Panel(cityState: true), Actions(pledge: false)), "TributeGold").Enabled);
    }

    [Fact]
    public void ProtectedCityStateOffersRevokeInsteadOfPledge()
    {
        var buttons = DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "Protector"), Panel("Protector", cityState: true), Actions(withdraw: true, pledge: true));
        Assert.DoesNotContain(buttons, it => it.Id == "PledgeProtection");
        var revoke = Button(buttons, "WithdrawProtection");
        Assert.True(revoke.Enabled);
        Assert.True(revoke.NeedsConfirm);
        Assert.IsType<WithdrawProtection>(revoke.Command);
        Assert.False(Button(DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "Protector"), Panel("Protector", cityState: true), Actions(withdraw: false)), "WithdrawProtection").Enabled);
    }

    [Fact]
    public void CityStateAtWarOffersGiftsAndPeaceButNoBullyingOrWar()
    {
        var buttons = DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "War"), Panel("War", cityState: true), Actions(negotiate: true, pledge: false));
        Assert.Equal(["Gift_250", "Gift_500", "Gift_1000", "PledgeProtection", "NegotiatePeace"], buttons.Select(it => it.Id));
        var peace = Button(buttons, "NegotiatePeace");
        Assert.True(peace.Enabled);
        Assert.IsType<NegotiateCityStatePeace>(peace.Command);
        Assert.False(Button(DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "War"), Panel("War", cityState: true), Actions(negotiate: false)), "NegotiatePeace").Enabled);
        Assert.All(buttons.Where(it => it.Kind == DiplomacyActionKind.Gift), it => Assert.False(it.Enabled));
    }

    [Fact]
    public void EveryButtonHasAUniqueIdAndAHint()
    {
        foreach (var (row, panel) in new[] { (RomeRow(), Panel()), (RomeRow(status: "War"), Panel("War")), (RomeRow(cityState: true), Panel(cityState: true)),
            (RomeRow(cityState: true, status: "Protector"), Panel("Protector", cityState: true)), (RomeRow(cityState: true, status: "War"), Panel("War", cityState: true)) })
        {
            var buttons = DiplomacyScreenModel.Buttons(row, panel, Actions());
            Assert.Equal(buttons.Count, buttons.Select(it => it.Id).Distinct().Count());
            Assert.All(buttons, it => Assert.False(string.IsNullOrWhiteSpace(it.Hint)));
            Assert.All(buttons, it => Assert.False(it.Enabled));
        }
    }

    // ---- C021 review: boundaries and fields the first tests could not tell apart

    [Fact]
    public void RowLabelUsesTheNameNotTheId() => Assert.Equal("Rome - Peace", new DiplomacyRow("civ-7", "Rome", false, "Peace").Label);

    [Fact]
    public void InfoLinesShowOneTurnOneOfferAndEachBorderOnItsOwn()
    {
        Assert.Contains("Peace treaty: war is forbidden for 1 more turns", DiplomacyScreenModel.InfoLines(Panel(treaty: 1)));
        Assert.DoesNotContain(DiplomacyScreenModel.InfoLines(Panel(treaty: 0)), it => it.StartsWith("Peace treaty", StringComparison.Ordinal));
        Assert.Contains("They sent you 1 trade offer(s)", DiplomacyScreenModel.InfoLines(Panel(offersFromThem: 1)));
        Assert.DoesNotContain(DiplomacyScreenModel.InfoLines(Panel(offersFromThem: 0)), it => it.StartsWith("They sent", StringComparison.Ordinal));
        Assert.Equal(["Status: Peace", "Attitude: Neutral", "Your units may cross their borders"], DiplomacyScreenModel.InfoLines(Panel(openOurs: true)));
        Assert.Equal(["Status: Peace", "Attitude: Neutral", "Their units may cross your borders"], DiplomacyScreenModel.InfoLines(Panel(openTheirs: true)));
        Assert.Equal(["Status: Peace", "Attitude: Neutral", "Your offer waits for their answer"], DiplomacyScreenModel.InfoLines(Panel(offerFromUs: true)));
    }

    [Fact]
    public void AQuestWithOneTurnLeftSaysSo() =>
        Assert.Equal(["Route: Go (+5 influence, 1 turns left)"], DiplomacyScreenModel.QuestLines(new CityStateQuestsResult("Kyiv", [new QuestEntry("Route", "Go", false, 1, 5f, "")])));

    [Fact]
    public void ProtectionButtonFollowsThePanelNotTheRow()
    {
        var protectedPanel = DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "Peace"), Panel("Protector", cityState: true), Actions(withdraw: true, pledge: true));
        Assert.Contains(protectedPanel, it => it.Id == "WithdrawProtection");
        Assert.DoesNotContain(protectedPanel, it => it.Id == "PledgeProtection");
        var unprotected = DiplomacyScreenModel.Buttons(RomeRow(cityState: true, status: "Protector"), Panel("Peace", cityState: true), Actions(withdraw: true, pledge: true));
        Assert.Contains(unprotected, it => it.Id == "PledgeProtection");
        Assert.DoesNotContain(unprotected, it => it.Id == "WithdrawProtection");
    }

    [Fact]
    public void OnlyWarDenounceAndRevokeNeedConfirmationAndEachHintSaysItsOwnThing()
    {
        var all = new List<DiplomacyButton>();
        foreach (var (row, panel) in new[] { (RomeRow(), Panel()), (RomeRow(status: "War"), Panel("War")), (RomeRow(cityState: true), Panel(cityState: true)),
            (RomeRow(cityState: true, status: "Protector"), Panel("Protector", cityState: true)), (RomeRow(cityState: true, status: "War"), Panel("War", cityState: true)) })
            all.AddRange(DiplomacyScreenModel.Buttons(row, panel, Actions(true, true, true, true, true, true, true, true, 250)));
        Assert.All(all, it => Assert.Equal(it.Kind is DiplomacyActionKind.DeclareWar or DiplomacyActionKind.Denounce or DiplomacyActionKind.WithdrawProtection, it.NeedsConfirm));
        var hints = all.GroupBy(it => it.Kind).ToDictionary(it => it.Key, it => it.First().Hint);
        Assert.Equal(hints.Count, hints.Values.Distinct().Count()); // one wording per kind
        Assert.Contains("tribute", hints[DiplomacyActionKind.TributeGold], StringComparison.Ordinal);
        Assert.Contains("worker", hints[DiplomacyActionKind.TributeWorker], StringComparison.Ordinal);
        Assert.Contains("peace treaty", hints[DiplomacyActionKind.DeclareWar], StringComparison.Ordinal);
    }
}
