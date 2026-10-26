using Unciv.Assets;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Assets;

public class NotificationsModelTests
{
    [Fact]
    public void APickerActionBeatsATileAndATechAndCarriesItsUnitId()
    {
        var promote = new NotificationActionDto { Kind = "promote", Id = 42 };
        var espionage = new NotificationActionDto { Kind = "espionage" };
        var tech = new NotificationActionDto { Kind = "tech", Name = "Mining" };
        Assert.Equal(new NotificationTarget(NotificationTargetKind.Promotion, 0, 0, null, 42), NotificationsModel.TargetOf([At("unit", 1, 2), promote]));
        Assert.Equal(new NotificationTarget(NotificationTargetKind.Spies, 0, 0, null), NotificationsModel.TargetOf([At("location", 1, 2), tech, espionage]));
        Assert.Equal(NotificationTargetKind.Spies, NotificationsModel.TargetOf([espionage, promote])!.Kind); // the engine's order within the pickers
        Assert.Equal(NotificationTargetKind.Promotion, NotificationsModel.TargetOf([promote, espionage])!.Kind);
    }

    [Fact]
    public void APromoteActionWithoutAUnitIsIgnored()
    {
        Assert.Null(NotificationsModel.TargetOf([new NotificationActionDto { Kind = "promote" }]));
        Assert.Equal(NotificationTargetKind.Tile, NotificationsModel.TargetOf([new NotificationActionDto { Kind = "promote" }, At("unit", 1, 2)])!.Kind);
    }

    private static NotificationDto Note(string text, string category = "General", params NotificationActionDto[] actions) =>
        new() { Category = category, Text = text, Icons = [], Actions = [.. actions] };

    private static NotificationActionDto At(string kind, int x, int y) => new() { Kind = kind, X = x, Y = y };

    [Fact]
    public void RowsAreNewestFirstKeepSnapshotIndexAndDropBrackets()
    {
        var rows = NotificationsModel.Rows([Note("[Rome] met you"), Note("second")]);
        Assert.Equal([1, 0], rows.Select(it => it.Index));
        Assert.Equal("Rome met you", rows[1].Text);
        Assert.Empty(NotificationsModel.Rows([]));
    }

    [Theory]
    [InlineData("location")]
    [InlineData("city")]
    [InlineData("unit")]
    public void PlaceActionsTargetTheTile(string kind)
    {
        var target = NotificationsModel.TargetOf([At(kind, 3, 4)]);
        Assert.Equal(new NotificationTarget(NotificationTargetKind.Tile, 3, 4, null), target);
    }

    [Fact]
    public void TheOriginTileIsAValidTarget()
    {
        Assert.Equal(new NotificationTarget(NotificationTargetKind.Tile, 0, 0, null), NotificationsModel.TargetOf([At("location", 0, 0)]));
    }

    [Fact]
    public void ATileBeatsATechAndTheFirstTileWins()
    {
        var tech = new NotificationActionDto { Kind = "tech", Name = "Mining" };
        Assert.Equal(NotificationTargetKind.Tile, NotificationsModel.TargetOf([tech, At("city", 1, 2), At("location", 5, 6)])!.Kind);
        Assert.Equal(1, NotificationsModel.TargetOf([tech, At("city", 1, 2), At("location", 5, 6)])!.X);
        Assert.Equal("Mining", NotificationsModel.TargetOf([tech])!.Name);
    }

    [Fact]
    public void ActionsWithoutAPlaceOrNameHaveNoTarget()
    {
        Assert.Null(NotificationsModel.TargetOf([]));
        Assert.Null(NotificationsModel.TargetOf([new NotificationActionDto { Kind = "location" }])); // coordinates stripped by the engine for an unseen tile
        Assert.Null(NotificationsModel.TargetOf([new NotificationActionDto { Kind = "tech", Name = "" }]));
        Assert.Null(NotificationsModel.TargetOf([new NotificationActionDto { Kind = "other", X = 1, Y = 1 }]));
    }

    [Fact]
    public void SummaryCountsByCategoryInEngineOrder()
    {
        var rows = NotificationsModel.Rows([Note("a", "War"), Note("b", "Cities"), Note("c", "War"), Note("d", "Zzz"), Note("e", "Production")]);
        Assert.Equal("Turn 3: 5 notifications (1 Production, 2 War, 1 Cities, 1 Zzz)", NotificationsModel.Summary(3, rows));
        Assert.Equal("Turn 4: 2 notifications (1 Aaa, 1 Zzz)", NotificationsModel.Summary(4, NotificationsModel.Rows([Note("a", "Zzz"), Note("b", "Aaa")])));
        Assert.Equal("Turn 3: 1 notification (1 War)", NotificationsModel.Summary(3, NotificationsModel.Rows([Note("a", "War")])));
        Assert.Equal("Turn 12: nothing new", NotificationsModel.Summary(12, []));
    }

    [Fact]
    public void ToggleLabelShowsCountAndState()
    {
        Assert.Equal("Notifications (4)", NotificationsModel.ToggleLabel(4, open: false));
        Assert.Equal("Hide notifications (0)", NotificationsModel.ToggleLabel(0, open: true));
    }
}
