using Godot;
using Unciv.Assets;
using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Settings;

/// <summary>T371: where the relay settings are read and written (<c>relay.json</c> in the real app; a temp file in scenarios and tests).</summary>
public sealed class RelaySettingsStore(Func<RelaySettings> load, Func<RelaySettings, bool> save)
{
    public RelaySettings Load() => load();

    /// <summary>False when the settings could not be written (T380).</summary>
    public bool Save(RelaySettings settings) => save(settings);
}

/// <summary>
/// T371 (design/RELAY.md sections 4 and 9): the "Multiplayer" page of the options. The relay address (checked by <see cref="RelaySettings.Problem"/> as it is typed), the optional fingerprint that pins the relay's own
/// certificate, the "Reach me through the relay" switch (only usable once a relay is set), what the relay can and cannot see, and the relays the player agreed to use for an invite, each with a Forget button.
/// A value is saved the moment it is valid; an invalid one says why and leaves the saved setting alone. There is no default relay: a fresh install shows empty fields.
/// </summary>
public sealed class RelaySettingsEditor
{
    private readonly UiTheme theme;
    private readonly RelaySettingsStore store;
    private readonly Label urlProblem = new() { Name = "RelayUrlProblem", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly Label saveProblem = new() { Name = "RelaySaveProblem", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly Label fingerprintProblem = new() { Name = "RelayFingerprintProblem", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
    private readonly VBoxContainer acceptedBox = new() { Name = "AcceptedRelays" };
    private readonly Label acceptedTitle = new() { Name = "AcceptedRelaysTitle", ThemeTypeVariation = UiVariants.LabelOverline };
    private readonly List<Action> translators = [];
    private bool loading;

    public RelaySettingsEditor(UiTheme theme, RelaySettingsStore store, Container page)
    {
        this.theme = theme;
        this.store = store;
        UrlField = new LineEdit { Name = "RelayUrlField", PlaceholderText = "unciv-relay://relay.example.org".Tr(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true }.FitMin(theme, 150);
        FingerprintField = new LineEdit { Name = "RelayFingerprintField", PlaceholderText = "(optional)".Tr(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true }.FitMin(theme, 150);
        ReachToggle = new CheckButton { Name = "ReachThroughRelayToggle", ThemeTypeVariation = UiVariants.ToggleSwitch, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var heading = new Label { ThemeTypeVariation = UiVariants.LabelOverline };
        Translate(() => heading.Text = "Relay".Tr());
        page.AddChild(heading);
        page.AddChild(Describe("A relay lets friends reach your game without opening a port, and lists public games. There is no default relay: enter the address your host or group uses.", UiVariants.LabelSecondary));
        page.AddChild(Labelled("Relay address", UrlField));
        page.AddChild(urlProblem);
        page.AddChild(Labelled("Relay fingerprint (pins a self-signed relay, optional)", FingerprintField));
        page.AddChild(fingerprintProblem);
        page.AddChild(saveProblem);
        Translate(() => ReachToggle.Text = "Reach me through the relay".Tr());
        page.AddChild(ReachToggle);
        page.AddChild(Describe("What the relay's operator can see: your IP address, which game room you join, when and for how long. Not the game, passwords or your name. A pinned host key from an invite is still checked.", UiVariants.LabelSecondary));
        Translate(() => acceptedTitle.Text = "Relays you agreed to (from invites)".Tr());
        page.AddChild(acceptedTitle);
        page.AddChild(acceptedBox);
        UrlField.TextChanged += _ => PastedOrTyped();
        FingerprintField.TextChanged += _ => Changed();
        UrlField.FocusExited += Normalize;
        UrlField.TextSubmitted += _ => Normalize();
        ReachToggle.Toggled += on =>
        {
            if (loading) return;
            var settings = store.Load();
            settings.ReachMeThroughRelay = on;
            Saved(store.Save(settings));
        };
        Reload();
    }

    public LineEdit UrlField { get; }

    public LineEdit FingerprintField { get; }

    /// <summary>The "Reach me through the relay" switch; disabled while no relay is set.</summary>
    public CheckButton ReachToggle { get; }

    /// <summary>The sentence under the address field when it is not a relay address, else empty.</summary>
    public string UrlProblem => urlProblem.Visible ? urlProblem.Text : "";

    /// <summary>T380: the sentence shown when the settings file could not be written, else empty.</summary>
    public string SaveProblem => saveProblem.Visible ? saveProblem.Text : "";

    public string FingerprintProblem => fingerprintProblem.Visible ? fingerprintProblem.Text : "";

    /// <summary>The relays (host:port) the player agreed to, with the Forget button of each.</summary>
    public IReadOnlyList<(string Relay, Button Forget)> Accepted => acceptedBox.GetChildren().OfType<HBoxContainer>().Select(it => (((Label)it.GetChild(0)).Text, (Button)it.GetChild(1))).ToList();

    /// <summary>Reads the stored settings into the controls (called when the screen opens, and after a Forget).</summary>
    public void Reload()
    {
        loading = true;
        try
        {
            var settings = store.Load();
            var (address, fingerprint) = RelaySettings.Split(settings.Url);
            UrlField.Text = address;
            FingerprintField.Text = fingerprint;
            ReachToggle.ButtonPressed = settings.ReachMeThroughRelay;
            ReachToggle.Disabled = settings.Endpoint == null;
            urlProblem.Visible = false;
            fingerprintProblem.Visible = false;
            saveProblem.Visible = false;
            FillAccepted(settings);
        }
        finally
        {
            loading = false;
        }
    }

    public void Retranslate()
    {
        foreach (var translate in translators) translate();
        urlProblem.Text = UrlProblemText;
        fingerprintProblem.Text = FingerprintProblemText;
    }

    // not translated through Tr: it would group the digits of the example port (47,830); the sentences carry addresses
    private string UrlProblemText => RelaySettings.Problem(RelaySettings.Compose(UrlField.Text, FingerprintField.Text))?? "";

    private string FingerprintProblemText => RelaySettings.FingerprintProblem(FingerprintField.Text)?? "";

    private void Translate(Action action)
    {
        translators.Add(action);
        action();
    }

    private Label Describe(string text, string variation)
    {
        var label = new Label { ThemeTypeVariation = variation, AutowrapMode = TextServer.AutowrapMode.WordSmart }.FitMin(theme, 150);
        Translate(() => label.Text = text.Tr());
        return label;
    }

    private VBoxContainer Labelled(string key, Control field)
    {
        var box = new VBoxContainer();
        box.FitConstant(theme, "separation", UiTokens.S1);
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }.FitMin(theme, 110);
        Translate(() => label.Text = key.Tr());
        box.AddChild(label);
        box.AddChild(field);
        return box;
    }

    // Saves when both fields are valid; says why when not, and leaves the saved setting as it was.
    private void Changed()
    {
        if (loading) return;
        var url = RelaySettings.Compose(UrlField.Text, FingerprintField.Text);
        var urlText = RelaySettings.Problem(url);
        var printText = RelaySettings.FingerprintProblem(FingerprintField.Text);
        urlProblem.Text = urlText?? "";
        urlProblem.Visible = urlText != null;
        fingerprintProblem.Text = printText?? "";
        fingerprintProblem.Visible = printText != null;
        if (urlText != null || printText != null) return;
        var settings = store.Load();
        settings.Url = string.IsNullOrWhiteSpace(url) ? null : url;
        Saved(store.Save(settings));
        ReachToggle.Disabled = settings.Endpoint == null;
    }

    // T380: a failed write is said out loud; every reader reloads from disk, so the change did not take effect.
    private void Saved(bool ok)
    {
        saveProblem.Text = SaveFailedText;
        saveProblem.Visible = !ok;
    }

    // not translated through Tr: it carries no number, but stays with the other problem sentences
    private const string SaveFailedText = "The relay settings could not be saved (is the user folder writable?). The change did not take effect.";

    // A full URL with ?fp= typed or pasted into the address field is split into the two fields at once, so the pasted fingerprint replaces the one already in the fingerprint field.
    private void PastedOrTyped()
    {
        if (loading) return;
        if (RelaySettings.Split(UrlField.Text).Fingerprint != "") Normalize();
        else Changed();
    }

    // Also on leaving the field.
    private void Normalize()
    {
        var (address, fingerprint) = RelaySettings.Split(UrlField.Text);
        if (fingerprint == "") return;
        loading = true;
        UrlField.Text = address;
        FingerprintField.Text = fingerprint;
        loading = false;
        Changed();
    }

    private void FillAccepted(RelaySettings settings)
    {
        foreach (var child in acceptedBox.GetChildren())
        {
            acceptedBox.RemoveChild(child);
            child.QueueFree();
        }
        acceptedTitle.Visible = settings.AcceptedRelays.Count > 0;
        foreach (var relay in settings.AcceptedRelays.ToList())
        {
            var row = new HBoxContainer();
            row.FitConstant(theme, "separation", UiTokens.S2);
            row.AddChild(new Label { Text = relay, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, VerticalAlignment = VerticalAlignment.Center }.FitMin(theme, 100));
            var forget = new Button { Text = "Forget".Tr(), Name = "Forget_" + relay };
            forget.Pressed += () =>
            {
                var current = store.Load();
                var saved = !current.Forget(relay) || store.Save(current);
                Reload();
                if (!saved) Saved(false);
            };
            row.AddChild(forget);
            acceptedBox.AddChild(row);
        }
    }
}
