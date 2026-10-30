using Godot;
using Unciv.Client.Net.Mods;
using Unciv.Client.Net.Session;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Net;
using Unciv.Models.Ruleset;
using Unciv.Net.Identity;
using Unciv.Net.Mods;

namespace Unciv.GodotClient.Setup;

/// <summary>What the join screen needs from the machine, as plain callbacks so a scenario can supply an in-memory host and fixed files.</summary>
public sealed class JoinScreenEnvironment
{
    public required ClientIdentity Identity { get; init; }

    /// <summary>The name this player appears under in the lobby.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The rules this client plays with; a host with other rules refuses the join (<see cref="LobbyModel.RefusalText"/>).</summary>
    public required Func<string> RulesetHash { get; init; }

    /// <summary>The pinned host fingerprints; the screen adds to it (with a trust answer) and calls <see cref="PersistKnownHosts"/>.</summary>
    public required KnownHosts KnownHosts { get; init; }

    public required Action<KnownHosts> PersistKnownHosts { get; init; }

    /// <summary>The address typed last time (shown in the field on opening).</summary>
    public string LastAddress { get; init; } = "";

    public Action<string> RememberAddress { get; init; } = _ => { };

    /// <summary>Opens the connection to the host. The real one is TCP + TLS (<c>TcpTlsClientTransport</c>); the host key is checked by the session, not here.</summary>
    public required Func<JoinTarget, CancellationToken, Task<ITransport>> Connect { get; init; }

    /// <summary>T247: where mods are installed (the file source the rulesets load from, and its mods folder). Without it a host with other mods is refused as before; with it the screen offers to download them.</summary>
    public IRulesetFileSource? ModFiles { get; init; }

    public string? ModsDirectory { get; init; }
}

/// <summary>
/// C006: the Join screen (the friend's side of C034). One field takes an address (<c>host:port</c>, the port defaults to 47821) or a pasted <c>unciv://</c> invite, one the password if the
/// host set one. An invite carries the host's fingerprint, so the connection is checked against it with no first-use window. A typed address that was never seen asks first: the
/// fingerprint is shown and nothing is sent to the host until the player trusts it, then it is pinned in the known hosts. A host whose key differs from the pinned one is a hard stop with no
/// "continue" button; only a deliberate "Forget the old key" lets a later attempt ask again. Known hosts are listed to pick again or forget. A joined session is handed on
/// (<see cref="Joined"/>) for the lobby screen; a refusal or failure is a sentence on this screen and nothing is left half-open.
/// </summary>
public partial class JoinScreen : Control
{
    private readonly JoinScreenEnvironment env;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly VBoxContainer hostsBox = new() { Name = "KnownHosts" };
    private readonly Label hostsTitle = new() { Name = "KnownHostsTitle" };
    private readonly Label messageLabel = new() { Name = "JoinMessage", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer formPanel = new() { Name = "JoinForm" };
    private readonly VBoxContainer trustPanel = new() { Name = "TrustPanel", Visible = false };
    private readonly Label trustLabel = new() { Name = "TrustText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer modPanel = new() { Name = "ModSyncPanel", Visible = false };
    private readonly Label modLabel = new() { Name = "ModSyncText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer stopPanel = new() { Name = "KeyChangedPanel", Visible = false };
    private readonly Label stopLabel = new() { Name = "KeyChangedText", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private CancellationTokenSource? attempt;
    private ClientSession? connecting;
    private TaskCompletionSource<bool>? trustAnswer;
    private TaskCompletionSource<bool>? modAnswer;
    private string? changedAddress;
    private bool busy;

    public JoinScreen(UiTheme theme, JoinScreenEnvironment env)
    {
        this.env = env;
        Name = "JoinScreen";
        this.Fill();
        var centre = new CenterContainer();
        centre.Fill();
        AddChild(centre);
        var column = new VBoxContainer().FitMin(theme, 420);
        centre.AddChild(column);
        column.AddChild(new Label { Text = "Join a game".Tr(), HorizontalAlignment = HorizontalAlignment.Center });

        column.AddChild(formPanel);
        formPanel.AddChild(new Label { Text = "Address or invite".Tr() });
        AddressField = new LineEdit { Name = "AddressField", PlaceholderText = "192.168.1.20:47821 or unciv://...".Tr(), Text = env.LastAddress };
        AddressField.TextSubmitted += _ => Join();
        formPanel.AddChild(AddressField);
        formPanel.AddChild(new Label { Text = "Password (only if the host set one)".Tr() });
        PasswordField = new LineEdit { Name = "PasswordField", Secret = true };
        PasswordField.TextSubmitted += _ => Join();
        formPanel.AddChild(PasswordField);
        var buttons = new HBoxContainer();
        JoinButton = new Button { Text = "Join".Tr(), Name = "JoinButton" };
        JoinButton.Pressed += Join;
        CancelButton = new Button { Text = "Cancel".Tr(), Name = "CancelButton", Visible = false };
        CancelButton.Pressed += CancelAttempt;
        BackButton = new Button { Text = "Back".Tr(), Name = "BackButton" };
        BackButton.Pressed += () =>
        {
            CancelAttempt();
            BackRequested?.Invoke();
        };
        buttons.AddChild(JoinButton);
        buttons.AddChild(CancelButton);
        buttons.AddChild(BackButton);
        formPanel.AddChild(buttons);
        hostsTitle.Text = "Known hosts".Tr();
        formPanel.AddChild(hostsTitle);
        formPanel.AddChild(hostsBox);

        column.AddChild(trustPanel);
        trustPanel.AddChild(trustLabel);
        var trustButtons = new HBoxContainer();
        TrustButton = new Button { Text = "Trust and join".Tr(), Name = "TrustButton" };
        TrustButton.Pressed += () => AnswerTrust(true);
        DistrustButton = new Button { Text = "Do not join".Tr(), Name = "DistrustButton" };
        DistrustButton.Pressed += () => AnswerTrust(false);
        trustButtons.AddChild(TrustButton);
        trustButtons.AddChild(DistrustButton);
        trustPanel.AddChild(trustButtons);

        column.AddChild(modPanel);
        modPanel.AddChild(modLabel);
        var modButtons = new HBoxContainer();
        DownloadButton = new Button { Text = "Download and join".Tr(), Name = "DownloadModsButton" };
        DownloadButton.Pressed += () => AnswerMods(true);
        DeclineButton = new Button { Text = "Do not download".Tr(), Name = "DeclineModsButton" };
        DeclineButton.Pressed += () => AnswerMods(false);
        modButtons.AddChild(DownloadButton);
        modButtons.AddChild(DeclineButton);
        modPanel.AddChild(modButtons);

        column.AddChild(stopPanel);
        stopPanel.AddChild(stopLabel);
        var stopButtons = new HBoxContainer();
        ForgetButton = new Button { Text = "Forget the old key".Tr(), Name = "ForgetButton" };
        ForgetButton.Pressed += ForgetChanged;
        StopBackButton = new Button { Text = "Back".Tr(), Name = "StopBackButton" };
        StopBackButton.Pressed += CloseStop;
        stopButtons.AddChild(StopBackButton);
        stopButtons.AddChild(ForgetButton);
        stopPanel.AddChild(stopButtons);

        column.AddChild(messageLabel);
        FillHosts();
    }

    public LineEdit AddressField { get; }

    public LineEdit PasswordField { get; }

    public Button JoinButton { get; }

    public Button CancelButton { get; }

    public Button BackButton { get; }

    public Button TrustButton { get; }

    public Button DistrustButton { get; }

    public Button DownloadButton { get; }

    public Button DeclineButton { get; }

    public Button ForgetButton { get; }

    public Button StopBackButton { get; }

    /// <summary>What the screen tells the player (a problem, progress, a refusal), or empty.</summary>
    public string Message => messageLabel.Text;

    /// <summary>The fingerprint prompt's text while it is open, else empty.</summary>
    public string TrustPrompt => trustPanel.Visible ? trustLabel.Text : "";

    /// <summary>The mod download prompt while it is open, else empty.</summary>
    public string ModPrompt => modPanel.Visible ? modLabel.Text : "";

    /// <summary>The base ruleset and mods of the host after a successful mod download (the rules this client now plays with), else null.</summary>
    public (string BaseRuleset, IReadOnlyList<string> Mods)? SyncedRules { get; private set; }

    /// <summary>The hard-stop text while it is open, else empty.</summary>
    public string KeyChangedPrompt => stopPanel.Visible ? stopLabel.Text : "";

    public bool IsBusy => busy;

    /// <summary>The known hosts as listed (address and pinned fingerprint), for pick and forget.</summary>
    public IReadOnlyList<string> HostLines => hostsBox.GetChildren().OfType<HBoxContainer>().Select(it => ((Button)it.GetChild(0)).Text).ToList();

    public Button? HostButton(string address) => FindHost(address, 0);

    public Button? ForgetHostButton(string address) => FindHost(address, 1);

    /// <summary>Raised when the player leaves the screen.</summary>
    public event Action? BackRequested;

    /// <summary>Raised when the host admitted this player; the receiver owns the session from here (lobby screen, leave, dispose).</summary>
    public event Action<ClientSession>? Joined;

    private Button? FindHost(string address, int index) =>
        hostsBox.GetChildren().OfType<HBoxContainer>().FirstOrDefault(it => it.Name == "Host-" + address)?.GetChild(index) as Button;

    private void FillHosts()
    {
        foreach (var child in hostsBox.GetChildren())
        {
            hostsBox.RemoveChild(child);
            child.QueueFree();
        }
        var pins = env.KnownHosts.Pins.OrderBy(it => it.Key, StringComparer.Ordinal).ToList();
        hostsTitle.Visible = pins.Count > 0;
        foreach (var (address, fingerprint) in pins)
        {
            var row = new HBoxContainer { Name = "Host-" + address };
            var pick = new Button { Text = address, TooltipText = HostKey.Normalize(fingerprint) ?? fingerprint, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            string picked = address;
            pick.Pressed += () => AddressField.Text = picked;
            var forget = new Button { Text = "Forget".Tr() };
            forget.Pressed += () =>
            {
                env.KnownHosts.Forget(picked);
                env.PersistKnownHosts(env.KnownHosts);
                FillHosts();
            };
            row.AddChild(pick);
            row.AddChild(forget);
            hostsBox.AddChild(row);
        }
    }

    /// <summary>Starts the join with what is typed. Does nothing while an attempt runs.</summary>
    public void Join()
    {
        if (busy) return;
        var parsed = JoinModel.Parse(AddressField.Text);
        if (parsed.Target is not { } target)
        {
            messageLabel.Text = (parsed.Problem ?? "").Tr();
            return;
        }
        _ = JoinAsync(target);
    }

    private async Task JoinAsync(JoinTarget target)
    {
        SetBusy(true, $"{"Connecting to".Tr()} {target.Address}...");
        attempt = new CancellationTokenSource();
        var cancellation = attempt.Token;
        var sync = env.ModFiles != null && env.ModsDirectory != null
            ? new ModSyncService(env.ModFiles, env.ModsDirectory, plan => AskModsAsync(plan, cancellation))
            : null;
        string? hashAfterSync = null; // after a mod download the host's own hash: the rules now are the host's
        SyncedRules = null;
        for (var round = 0; ; round++)
        {
            var session = new ClientSession(token => env.Connect(target, token), new ClientSessionOptions
            {
                Identity = env.Identity,
                DisplayName = env.DisplayName,
                RulesetHash = hashAfterSync ?? env.RulesetHash(),
                Password = string.IsNullOrEmpty(PasswordField.Text) ? null : PasswordField.Text,
                InviteToken = target.Token,
                ExpectedFingerprint = target.Fingerprint,
                HostAddress = target.Address,
                KnownHosts = env.KnownHosts,
                PersistKnownHosts = env.PersistKnownHosts,
                TrustPrompt = (address, fingerprint) => AskTrustAsync(address, fingerprint, cancellation),
                // T273: a host that quits cleanly hands the game to its backup host; the session follows the announced endpoint (its fingerprint is checked by the session itself).
                ConnectEndpoint = (endpoint, token) => JoinModel.Parse(endpoint).Target is { } successor ? env.Connect(successor, token) : throw new InvalidOperationException("The announced endpoint is not usable"),
                ModChecksums = round == 0 ? sync?.LocalChecksums() : null,
                ModSync = round == 0 ? sync : null, // one download per attempt: a host that still disagrees afterwards is refused, never looped on
            });
            connecting = session;
            JoinResult result;
            try
            {
                result = await session.JoinAsync().ConfigureAwait(true);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                result = new JoinResult(false, ClientCodes.ConnectFailed);
            }

            if (cancellation.IsCancellationRequested || connecting != session) return; // cancelled: CancelAttempt already disposed the session and reset the screen
            if (result.Joined)
            {
                connecting = null;
                attempt.Dispose();
                attempt = null;
                env.RememberAddress(target.Address); // never the invite text itself: it may carry a one-time token
                SetBusy(false, "");
                Joined?.Invoke(session);
                return;
            }

            if (result.Code == ModSyncCodes.Synced && sync != null && session.ModSyncResult is { Manifest: { } manifest } outcome)
            {
                await session.DisposeAsync().ConfigureAwait(true);
                if (cancellation.IsCancellationRequested) return;
                // The mods are on disk; make them live (this is the thread that owns the ruleset cache) and join again with the host's rules.
                var failure = sync.HotLoad(outcome);
                if (failure != null)
                {
                    EndAttempt(JoinModel.RefusalText(failure));
                    return;
                }
                SyncedRules = (manifest.BaseRuleset, manifest.Mods);
                hashAfterSync = manifest.RulesetHash;
                messageLabel.Text = $"{"Mods installed. Joining...".Tr()}";
                continue;
            }

            connecting = null;
            attempt.Dispose();
            attempt = null;
            await session.DisposeAsync().ConfigureAwait(true);
            SetBusy(false, "");
            FillHosts(); // a key trusted during this attempt is pinned now
            string code = result.Code ?? ClientCodes.ConnectFailed;
            if (code == ClientCodes.HostKeyChanged)
            {
                changedAddress = target.Address;
                stopLabel.Text = JoinModel.KeyChangedText(target.Address);
                ShowPanel(stopPanel);
                return;
            }
            messageLabel.Text = JoinModel.RefusalText(code).Tr();
            return;
        }
    }

    private void EndAttempt(string message)
    {
        connecting = null;
        attempt?.Dispose();
        attempt = null;
        SetBusy(false, "");
        FillHosts();
        messageLabel.Text = message.Tr();
    }

    // The session asks from its own thread; the answer waits for the player's button on the main thread.
    private Task<bool> AskModsAsync(ModSyncPlan plan, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation.Register(() => answer.TrySetResult(false));
        void Show()
        {
            if (cancellation.IsCancellationRequested) return;
            modAnswer = answer;
            modLabel.Text = JoinModel.ModSyncText(plan.ToDownload.Select(it => (it.Name, it.Status.ToString(), it.Bytes)).ToList());
            ShowPanel(modPanel);
            messageLabel.Text = "";
        }
        if (context == null) Callable.From(Show).CallDeferred();
        else context.Post(static state => ((Action)state!)(), (Action)Show);
        return answer.Task;
    }

    private void AnswerMods(bool download)
    {
        var answer = modAnswer;
        modAnswer = null;
        ShowPanel(formPanel);
        if (download) messageLabel.Text = "Downloading mods...".Tr();
        answer?.TrySetResult(download);
    }

    // The session asks from its own thread; the answer waits for the player's button on the main thread.
    private Task<bool> AskTrustAsync(string address, string fingerprint, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation.Register(() => answer.TrySetResult(false));
        void Show()
        {
            if (cancellation.IsCancellationRequested) return;
            trustAnswer = answer;
            trustLabel.Text = JoinModel.TrustText(address, fingerprint);
            ShowPanel(trustPanel);
            messageLabel.Text = "";
        }
        if (context == null) Callable.From(Show).CallDeferred();
        else context.Post(static state => ((Action)state!)(), (Action)Show);
        return answer.Task;
    }

    private void AnswerTrust(bool trusted)
    {
        var answer = trustAnswer;
        trustAnswer = null;
        ShowPanel(formPanel);
        if (trusted) messageLabel.Text = "Connecting...".Tr();
        answer?.TrySetResult(trusted);
    }

    private void ForgetChanged()
    {
        if (changedAddress != null)
        {
            env.KnownHosts.Forget(changedAddress);
            env.PersistKnownHosts(env.KnownHosts);
            FillHosts();
            messageLabel.Text = $"{"Forgot the old key of".Tr()} {changedAddress}. {"Joining again will ask about the new one.".Tr()}";
        }
        changedAddress = null;
        ShowPanel(formPanel);
    }

    private void CloseStop()
    {
        changedAddress = null;
        ShowPanel(formPanel);
    }

    private void CancelAttempt()
    {
        if (!busy) return;
        attempt?.Cancel();
        var pending = connecting;
        connecting = null;
        if (trustAnswer is { } answer)
        {
            trustAnswer = null;
            answer.TrySetResult(false);
        }
        if (modAnswer is { } modsAnswer)
        {
            modAnswer = null;
            modsAnswer.TrySetResult(false);
        }
        ShowPanel(formPanel);
        SetBusy(false, "Cancelled.".Tr());
        if (pending != null) _ = pending.DisposeAsync().AsTask();
    }

    private void SetBusy(bool value, string message)
    {
        busy = value;
        JoinButton.Disabled = value;
        CancelButton.Visible = value;
        AddressField.Editable = !value;
        PasswordField.Editable = !value;
        messageLabel.Text = message;
    }

    private void ShowPanel(Control panel)
    {
        formPanel.Visible = panel == formPanel;
        trustPanel.Visible = panel == trustPanel;
        modPanel.Visible = panel == modPanel;
        stopPanel.Visible = panel == stopPanel;
    }
}
