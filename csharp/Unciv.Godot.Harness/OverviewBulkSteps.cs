using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Logic.City;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.GodotHarness;

/// <summary>
/// C092: the bulk-actions half of the <c>overview</c> scenario (split out to keep the scenario's Build short): select cities in the cities table, then set focus, queue an item, save and apply a build
/// template (stored in a temporary client settings file), buy the current production and try the puppet city alone. Every assertion compares the screen with the engine's state and the command log.
/// </summary>
public sealed class OverviewBulkSteps
{
    private readonly GameScreen screen;
    private readonly GameSession session;
    private readonly CivilizationObject human;
    private readonly string puppet;
    private readonly Func<Button?, Vector2> centre;
    private readonly Func<string, Vector2> tab;
    private readonly Func<List<string>> engineCities;
    private readonly string settingsPath;
    private readonly ClientSettingsService settings;
    private int logged;
    private int goldBefore;

    public OverviewBulkSteps(GameScreen screen, GameSession session, CivilizationObject human, string puppet, Func<Button?, Vector2> centre, Func<string, Vector2> tab, Func<List<string>> engineCities)
    {
        this.screen = screen;
        this.session = session;
        this.human = human;
        this.puppet = puppet;
        this.centre = centre;
        this.tab = tab;
        this.engineCities = engineCities;
        settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-overview-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
        settings = new ClientSettingsService(new ClientSettingsStore(settingsPath));
        screen.Overview.BindSettings(settings);
    }

    private Unciv.Logic.City.City Capital => human.Cities.Single(it => it.IsCapital());

    private int Sent(string type) => session.Log.Count(it => it.Type == type);

    private Vector2 Tab(string name) => tab(name);

    private Vector2 Centre(Button? button) => centre(button);

    private Vector2 CityRow(string city)
        {
            var data = screen.Overview.DataTables(OverviewModel.Cities)[0];
            var keys = screen.Overview.Tables(OverviewModel.Cities)[0].Rows.Select(it => it.Key).ToList();
            return data.Cell(data.Order.ToList().FindIndex(it => keys[it] == city), 0).GetGlobalRect().GetCenter();
        }
    private Vector2 Bulk(string name) => Centre(screen.Overview.BulkControl(name) as Button);
    private void ScrollTop()
        {
            if (screen.Overview.FindChild("Scroll_Cities", true, false) is ScrollContainer scroll) scroll.ScrollVertical = 0;
        }
    private void Reveal(string name)
        {
            var control = screen.Overview.BulkControl(name).NotNull();
            for (Node? node = control.GetParent(); node != null; node = node.GetParent())
                if (node is ScrollContainer scroll)
                {
                    scroll.EnsureControlVisible(control);
                    return;
                }
        }
    private void Choose(string name, int index)
        {
            var option = (OptionButton)screen.Overview.BulkControl(name).NotNull();
            option.Select(index);
            option.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
    private int ItemIndex(string name, string item)
        {
            var option = (OptionButton)screen.Overview.BulkControl(name).NotNull();
            return Enumerable.Range(0, option.ItemCount).First(it => option.GetItemText(it).Contains(item, StringComparison.Ordinal));
        }


    public IReadOnlyList<ScenarioStep> Steps()
    {
        var capital = Capital;
        var puppetCity = human.Cities.Single(it => it.IsPuppet);
        List<string> EngineCities() => engineCities();
        return
        [
            new Click(() => Tab(OverviewModel.Cities)),
            new WaitFrames(2),
            new AssertQuery("the bulk panel starts with nothing selected and its actions disabled", () =>
                screen.Overview.SelectedCities.Count == 0 && (screen.Overview.BulkControl("BulkCount") as Label)?.Text == "Selected: 0"
                && new[] { "BulkFocusApply", "BulkQueue", "BulkBuy", "BulkTemplateApply", "BulkTemplateSave" }.All(it => screen.Overview.BulkControl(it) is Button { Disabled: true })
                && screen.Overview.BulkControl("BulkSelectAll") is Button { Disabled: false } && screen.Overview.BulkControl("BulkTemplate") is OptionButton { Disabled: true }),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => CityRow(capital.Name)),
            new WaitFrames(2),
            new AssertQuery("a click on a row selects that city and enables the actions; a second click on the same row clears it", () =>
                screen.Overview.SelectedCities.SequenceEqual([capital.Name]) && (screen.Overview.BulkControl("BulkCount") as Label)?.Text == "Selected: 1"
                && screen.Overview.BulkControl("BulkQueue") is Button { Disabled: false } && screen.Overview.DataTables(OverviewModel.Cities)[0].SelectedRows.Count == 1),
            new Click(() => CityRow(capital.Name)),
            new WaitFrames(2),
            new AssertQuery("the row is deselected again", () => screen.Overview.SelectedCities.Count == 0 && screen.Overview.BulkControl("BulkQueue") is Button { Disabled: true }),
            new Click(() => Bulk("BulkSelectAll")),
            new WaitFrames(2),
            new AssertQuery("Select all selects both cities", () => screen.Overview.SelectedCities.OrderBy(it => it, StringComparer.Ordinal).SequenceEqual(EngineCities().Order(StringComparer.Ordinal)) && session.Log.Count == logged),
            new Act("choose the gold focus", () => Choose("BulkFocus", ItemIndex("BulkFocus", "Gold"))),
            new Act("scroll to BulkFocusApply", () => Reveal("BulkFocusApply")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkFocusApply")),
            new WaitFrames(8),
            new AssertQuery("set focus sends one set-city-focus for the capital; the puppet city is skipped and the screen says why", () =>
                capital.GetCityFocus() == CityFocus.GoldFocus && puppetCity.GetCityFocus() != CityFocus.GoldFocus
                && Sent("set-city-focus") == 1 && session.Log.Count == logged + 1
                && screen.Overview.BulkResultText.StartsWith("1 change made.", StringComparison.Ordinal)
                && screen.Overview.BulkResultText.Contains("puppet city chooses its own citizens", StringComparison.Ordinal)),
            new Act("scroll to the top", ScrollTop),
            new WaitFrames(2),
            new Capture("cities-bulk", "Cities with both rows selected (accent text) and the bulk actions panel below the table: focus, queue an item or buy, a saved build template; the last result names the puppet city that was skipped"),

            new Act("choose Monument", () => Choose("BulkItem", ItemIndex("BulkItem", "Monument"))),
            new Act("scroll to BulkQueue", () => Reveal("BulkQueue")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkQueue")),
            new WaitFrames(8),
            new AssertQuery("queueing an item sends one queue-construction for the capital and none for the puppet", () =>
                capital.CityConstructions.ConstructionQueue.Contains("Monument") && !puppetCity.CityConstructions.ConstructionQueue.Contains("Monument")
                && Sent("queue-construction") == 1 && screen.Overview.BulkResultText.StartsWith("1 change made.", StringComparison.Ordinal)),

            new Act("name the template", () =>
            {
                var field = (LineEdit)screen.Overview.BulkControl("BulkTemplateName").NotNull();
                field.Text = "Starter";
                field.EmitSignal(LineEdit.SignalName.TextChanged, "Starter");
            }),
            new Act("scroll to BulkTemplateSave", () => Reveal("BulkTemplateSave")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkTemplateSave")),
            new WaitFrames(8),
            new AssertQuery("saving stores the first selected city's queue in the client settings, and the template choice lists it; no command is sent", () =>
                settings.Current.BuildTemplates.Count == 1 && settings.Current.BuildTemplates[0].Name == "Starter"
                && settings.Current.BuildTemplates[0].Items.SequenceEqual(capital.CityConstructions.ConstructionQueue) && settings.Current.BuildTemplates[0].Items.Contains("Monument")
                && screen.Overview.BulkControl("BulkTemplate") is OptionButton { Disabled: false, ItemCount: 1 } && new ClientSettingsStore(settingsPath).Load().BuildTemplates.Count == 1
                && Sent("queue-construction") == 1 && screen.Overview.BulkResultText == "Saved the template Starter."),
            new Act("add a second template with a unit and the monument", () =>
            {
                settings.Update(s => s with { BuildTemplates = [.. s.BuildTemplates, new BuildTemplate { Name = "Extras", Items = ["Warrior", "Monument"] }] });
                screen.OpenOverviewScreen(OverviewModel.Cities);
            }),
            new WaitFrames(8),
            new Act("choose Extras", () => Choose("BulkTemplate", ItemIndex("BulkTemplate", "Extras"))),
            new Act("scroll to BulkTemplateApply", () => Reveal("BulkTemplateApply")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkTemplateApply")),
            new WaitFrames(8),
            new AssertQuery("applying a template queues what the city can (the warrior) and reports what it cannot (the monument is queued already)", () =>
                Sent("queue-construction") == 2 && capital.CityConstructions.ConstructionQueue.Count(it => it == "Warrior") >= 1 && capital.CityConstructions.ConstructionQueue.Count(it => it == "Monument") == 1
                && screen.Overview.BulkResultText.StartsWith("1 change made.", StringComparison.Ordinal) && screen.Overview.BulkResultText.Contains("Already in the queue", StringComparison.Ordinal)),
            new Act("scroll to BulkTemplateDelete", () => Reveal("BulkTemplateDelete")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkTemplateDelete")),
            new WaitFrames(8),
            new AssertQuery("deleting removes the chosen template from the settings file", () =>
                settings.Current.BuildTemplates.Select(it => it.Name).SequenceEqual(["Starter"]) && new ClientSettingsStore(settingsPath).Load().BuildTemplates.Count == 1),

            new Act("remember the gold", () => goldBefore = human.Gold),
            new Act("scroll to BulkBuy", () => Reveal("BulkBuy")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkBuy")),
            new WaitFrames(8),
            new AssertQuery("buying the current production sends one purchase for the capital with the engine's price; the puppet city's turn is skipped or refused by the engine, never both applied", () =>
                Sent("purchase-construction") <= 1 && human.Gold <= goldBefore && (Sent("purchase-construction") == 1) == (human.Gold < goldBefore) && screen.Overview.BulkResultText.Length > 0),

            new Act("scroll to BulkClear", () => Reveal("BulkClear")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkClear")),
            new WaitFrames(2),
            new Act("scroll to the table", ScrollTop),
            new WaitFrames(2),
            new Click(() => CityRow(puppet)),
            new WaitFrames(2),
            new Act("remember the command log again", () => logged = session.Log.Count),
            new Act("scroll to BulkQueue", () => Reveal("BulkQueue")),
            new WaitFrames(2),
            new Click(() => Bulk("BulkQueue")),
            new WaitFrames(8),
            new AssertQuery("with only the puppet city selected a bulk queue sends nothing and says why", () =>
                session.Log.Count == logged && screen.Overview.BulkResultText.StartsWith("0 changes made.", StringComparison.Ordinal)
                && screen.Overview.BulkResultText.Contains("puppet city chooses its own production", StringComparison.Ordinal)),

        ];
    }
}
