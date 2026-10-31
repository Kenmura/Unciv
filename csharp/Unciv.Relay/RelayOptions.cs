using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Unciv.Relay;

/// <summary>Thrown for a command line or config file the operator got wrong; the runner prints the message and exits with code 2.</summary>
public sealed class RelayUsageException(string message) : Exception(message);

/// <summary>Which listings the directory accepts (RELAY.md section 6, A8).</summary>
public enum ListingPolicy
{
    Open,
    Allowlist,
    Off,
}

/// <summary>How the tunnel port is secured (RELAY.md section 8).</summary>
public enum RelayTlsMode
{
    /// <summary><c>--cert</c> and <c>--key</c>: a CA certificate, re-read when the files change.</summary>
    Certificate,

    /// <summary><c>--self-signed</c>: a generated certificate whose fingerprint clients pin.</summary>
    SelfSigned,

    /// <summary><c>--tls none</c>: plain TCP, for a TLS-terminating proxy in front (nginx stream).</summary>
    None,
}

/// <summary>Every limit of RELAY.md section 6 with its default; each is a flag and a <c>relay.json</c> key of the same name.</summary>
public sealed record RelayLimits
{
    public int MaxRooms { get; init; } = 200;

    public int MaxSplices { get; init; } = 600;

    public int MaxControlPerNetwork { get; init; } = 4;

    public int MaxRoomsPerNetwork { get; init; } = 2;

    public int MaxConnectionsPerNetwork { get; init; } = 40;

    public int MaxJoinersPerRoom { get; init; } = 16;

    public int MaxPendingPerRoom { get; init; } = 8;

    public int MaxJoinersPerNetwork { get; init; } = 3;

    public int HandshakesPerMinute { get; init; } = 30;

    public int JoinsPerMinute { get; init; } = 10;

    public int LookupFailuresPerMinute { get; init; } = 10;

    public int BanMinutes { get; init; } = 10;

    public int ListingsPerMinute { get; init; } = 6;

    public int HttpRequestsPerMinute { get; init; } = 30;

    /// <summary>Sustained bytes per second of one splice.</summary>
    public long SpliceRate { get; init; } = 2L * 1024 * 1024;

    /// <summary>Burst size of one splice, bytes (a 32 MiB snapshot must pass).</summary>
    public long SpliceBurst { get; init; } = 48L * 1024 * 1024;

    public long RoomBytesPerDay { get; init; } = 20L * 1024 * 1024 * 1024;

    public int IdleSeconds { get; init; } = 120;

    public int ControlSilenceSeconds { get; init; } = 90;

    public int ListingStaleMinutes { get; init; } = 30;

    public int RoomTtlHours { get; init; } = 24;

    public int HostTimeoutMillis { get; init; } = 10_000;

    public int PreambleTimeoutMillis { get; init; } = 5_000;

    public int FirstBytesSeconds { get; init; } = 10;

    /// <summary>Global egress cap in megabits per second; 0 = off.</summary>
    public int MaxMbps { get; init; }

    /// <summary>Global cap per 30 days in GiB; 0 = off.</summary>
    public int MaxGibPerMonth { get; init; }
}

/// <summary>The parsed settings of the relay (T337): command line over <c>relay.json</c> over defaults. <see cref="Parse"/> throws <see cref="RelayUsageException"/>; nothing here touches the network.</summary>
public sealed record RelayOptions
{
    public const int DefaultTunnelPort = 47830;
    public const int DefaultHttpPort = 47831;

    public const string HelpText = """
        Unciv.Relay - relay server: lets a host without open ports reach joiners, and lists public games (design/RELAY.md).
        It only accepts connections, never dials one, and never sees game data: the end-to-end TLS of the game runs through it.

        Usage: Unciv.Relay [options]      (every option can also be a key of the same name in DATA-DIR/relay.json or --config FILE; the command line wins)

        TLS of the tunnel port (choose exactly one):
          --cert FILE --key FILE   A certificate (PEM chain) and its key; re-read on SIGHUP and when the files change (certbot renewal).
          --self-signed            Generate DATA-DIR/relay.pfx and print 'RELAY-FINGERPRINT <fp>'; clients pin it (unciv-relay://host:port?fp=...). Needs --public-name.
          --tls none               Plain TCP, for nginx stream in front (it terminates TLS). Listens on loopback unless --bind is given. Needs --public-name.
          --public-name NAME       The relay name hosts sign (repeatable; default: the DNS names of --cert). An IP literal is fine.
          --proxy-protocol         Read a PROXY protocol v1/v2 header first, from --trusted-proxy sources only (anything else is closed).
          --trusted-proxy CIDR     Repeatable; default 127.0.0.1/32 and ::1/128.

        Listeners:
          --tunnel-port N          TCP port of the tunnel (default 47830; 0 = a free one). Printed as 'LISTENING tunnel <port>'.
          --bind ADDR              Address for the tunnel (default all interfaces; loopback with --tls none).
          --http-port N            Read-only directory over HTTP (default 47831; 0 = a free one). Printed as 'LISTENING http <port>'.
          --http-bind ADDR         Default 127.0.0.1.        --no-http  Do not start it.        --http-tls  Serve it over HTTPS with the tunnel certificate.
          --metrics                Serve Prometheus text on GET /metrics.

        Policy:
          --listing open|allowlist|off   Who may list games in the directory (default open; allowlist reads DATA-DIR/allow-keys.txt; off = no directory).
          --name TEXT --contact TEXT --motd TEXT   Shown by GET /v1/info (put an abuse contact in --contact).
          --data-dir DIR           relay.json, relay-secret, relay.pfx, bans.txt, allow-keys.txt, deny-names.txt, logs/ (default ./unciv-relay-data).
          bans.txt lines: an IP or CIDR; 'key <fingerprint>'; 'name <regex>'. deny-names.txt: one regex per line. Reloaded on SIGHUP and when the file changes.

        Limits (defaults in brackets): --max-rooms [200] --max-splices [600] --max-control-per-network [4] --max-rooms-per-network [2] --max-connections-per-network [40]
          --max-joiners-per-room [16] --max-pending-per-room [8] --max-joiners-per-network [3] --handshakes-per-minute [30] --joins-per-minute [10]
          --lookup-failures-per-minute [10] --ban-minutes [10] --listings-per-minute [6] --http-requests-per-minute [30] --splice-rate BYTES/S [2097152]
          --splice-burst BYTES [50331648] --room-bytes-per-day [21474836480] --idle-seconds [120] --control-silence-seconds [90] --listing-stale-minutes [30]
          --room-ttl-hours [24] --host-timeout-millis [10000] --preamble-timeout-millis [5000] --first-bytes-seconds [10] --max-mbps [0=off] --max-gib-per-month [0=off]

        Logs: one line per event on stdout and in DATA-DIR/logs/relay-<date>.log, never any game data. --log-level debug|info|warn|error, --log-retention-days N [7], --no-log-files.
        --drain-seconds N [10]: how long a stop waits for running splices after telling hosts to reconnect.

        Stopping: SIGTERM or SIGINT stop accepting, tell every host 'closing: restart', drain, exit 0. SIGHUP reloads certificates and the ban/allow/deny files.
        Exit codes: 0 stopped normally, 1 failure while running (port in use, unreadable certificate), 2 bad command line or config.
        """;

    public bool Help { get; init; }

    public string DataDir { get; init; } = "unciv-relay-data";

    public int TunnelPort { get; init; } = DefaultTunnelPort;

    public string? Bind { get; init; }

    public RelayTlsMode? TlsMode { get; init; }

    public string? CertFile { get; init; }

    public string? KeyFile { get; init; }

    public IReadOnlyList<string> PublicNames { get; init; } = [];

    public bool ProxyProtocol { get; init; }

    public IReadOnlyList<IPNetwork> TrustedProxies { get; init; } = [new IPNetwork(IPAddress.Loopback, 32), new IPNetwork(IPAddress.IPv6Loopback, 128)];

    public int HttpPort { get; init; } = DefaultHttpPort;

    public string HttpBind { get; init; } = "127.0.0.1";

    public bool NoHttp { get; init; }

    public bool HttpTls { get; init; }

    public bool Metrics { get; init; }

    public ListingPolicy Listing { get; init; } = ListingPolicy.Open;

    public string Name { get; init; } = "Unciv relay";

    public string Contact { get; init; } = "";

    public string Motd { get; init; } = "";

    public RelayLimits Limits { get; init; } = new();

    public string LogLevel { get; init; } = "info";

    public int LogRetentionDays { get; init; } = 7;

    public bool NoLogFiles { get; init; }

    public int DrainSeconds { get; init; } = 10;

    /// <summary>How often the background sweep runs (expiry, idle splices, file reload); tests call the sweep themselves.</summary>
    public int SweepIntervalMillis { get; init; } = 1000;

    private static readonly string[] Flags = ["self-signed", "proxy-protocol", "no-http", "http-tls", "metrics", "no-log-files"];

    private static readonly string[] Texts =
        ["data-dir", "config", "bind", "tls", "cert", "key", "public-name", "trusted-proxy", "http-bind", "listing", "name", "contact", "motd", "log-level"];

    /// <summary>Numeric options: name, minimum, maximum. The maximum keeps arithmetic on them overflow-free.</summary>
    private static readonly (string Name, long Min, long Max)[] Numbers =
    [
        ("tunnel-port", 0, 65535), ("http-port", 0, 65535), ("max-rooms", 1, 100_000), ("max-splices", 1, 1_000_000), ("max-control-per-network", 1, 1000),
        ("max-rooms-per-network", 1, 1000), ("max-connections-per-network", 1, 100_000), ("max-joiners-per-room", 1, 1000), ("max-pending-per-room", 1, 1000),
        ("max-joiners-per-network", 1, 1000), ("handshakes-per-minute", 1, 1_000_000), ("joins-per-minute", 1, 1_000_000), ("lookup-failures-per-minute", 1, 1_000_000),
        ("ban-minutes", 1, 525_600), ("listings-per-minute", 1, 1_000_000), ("http-requests-per-minute", 1, 1_000_000), ("splice-rate", 1, 1L << 40), ("splice-burst", 1, 1L << 40),
        ("room-bytes-per-day", 1, 1L << 50), ("idle-seconds", 1, 86_400), ("control-silence-seconds", 1, 86_400), ("listing-stale-minutes", 1, 525_600), ("room-ttl-hours", 1, 8760),
        ("host-timeout-millis", 1, 600_000), ("preamble-timeout-millis", 1, 600_000), ("first-bytes-seconds", 1, 3600), ("max-mbps", 0, 1_000_000), ("max-gib-per-month", 0, 1_000_000),
        ("log-retention-days", 1, 3650), ("drain-seconds", 0, 3600), ("sweep-interval-millis", 10, 3_600_000),
    ];

    public static RelayOptions Parse(IReadOnlyList<string> args)
    {
        var raw = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h") return new RelayOptions { Help = true };
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2) throw new RelayUsageException($"unexpected argument '{arg}'");
            var name = arg[2..];
            if (Flags.Contains(name)) { raw[name] = ["true"]; continue; }
            if (!IsKnown(name)) throw new RelayUsageException($"unknown option --{name} (see --help)");
            if (i + 1 >= args.Count) throw new RelayUsageException($"option --{name} needs a value");
            if (!Repeatable(name) && raw.ContainsKey(name)) throw new RelayUsageException($"option --{name} given twice");
            Add(raw, name, args[++i]);
        }
        return BindOptions(raw);
    }

    /// <summary>Prefix of the environment variables that set an option (T374): <c>UNCIV_RELAY_</c> plus the option name in upper case with <c>-</c> as <c>_</c>.</summary>
    public const string EnvPrefix = "UNCIV_RELAY_";

    /// <summary>Every option name the command line and <c>relay.json</c> accept (without the <c>--</c>).</summary>
    public static IReadOnlyList<string> OptionNames => [.. Flags, .. Texts, .. Numbers.Select(n => n.Name)];

    /// <summary>The environment variable that sets the option <paramref name="optionName"/>, for example <c>max-rooms</c> gives <c>UNCIV_RELAY_MAX_ROOMS</c>.</summary>
    public static string EnvName(string optionName) => EnvPrefix + optionName.ToUpperInvariant().Replace('-', '_');

    /// <summary>
    /// Applies the options of a <c>relay.json</c> object (key = option name, bool for flags, array for repeatable options) below the ones already set. With <paramref name="readEnv"/>
    /// (T374) the environment is a third, lowest layer: command line over <c>relay.json</c> over <c>UNCIV_RELAY_*</c> variables over defaults. An unset or empty variable is ignored; a flag is on for
    /// <c>true</c> or <c>1</c> (any case) and off for <c>false</c>, <c>0</c> or empty; a repeatable option takes a comma-separated list.
    /// </summary>
    public static RelayOptions ParseWithConfig(IReadOnlyList<string> args, Func<string, string?> readFile, Func<string, string?>? readEnv = null)
    {
        if (args.Any(a => a is "--help" or "-h")) return new RelayOptions { Help = true };
        var env = ReadEnvironment(readEnv);
        var configPath = FindOption(args, "config") ?? env.GetValueOrDefault("config")?[0] ?? Path.Combine(FindOption(args, "data-dir") ?? env.GetValueOrDefault("data-dir")?[0] ?? new RelayOptions().DataDir, "relay.json");
        var explicitConfig = FindOption(args, "config") != null || env.ContainsKey("config");
        var text = readFile(configPath);
        if (text == null && explicitConfig) throw new RelayUsageException($"config file '{configPath}' cannot be read");
        var raw = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (text != null) ReadConfigFile(text, configPath, raw);
        // the command line wins over the file, the file over the environment: drop every key a higher layer set, then re-parse the layers in order
        var merged = new Dictionary<string, List<string>>(env, StringComparer.Ordinal);
        foreach (var (key, values) in raw) merged[key] = values;
        foreach (var key in CliKeys(args)) merged.Remove(key);
        merged.Remove("config");
        if (merged.Count == 0) return Parse(args);
        var layered = new List<string>();
        foreach (var (key, values) in merged)
        {
            if (Flags.Contains(key)) { if (values[0] == "true") layered.Add("--" + key); continue; }
            foreach (var value in values) { layered.Add("--" + key); layered.Add(value); }
        }
        layered.AddRange(args);
        return Parse(layered);
    }

    private static Dictionary<string, List<string>> ReadEnvironment(Func<string, string?>? readEnv)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (readEnv == null) return result;
        foreach (var name in OptionNames)
        {
            var value = readEnv(EnvName(name))?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            if (Flags.Contains(name))
            {
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1") result[name] = ["true"];
                else if (!value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0") throw new RelayUsageException($"{EnvName(name)} must be true, 1, false or 0, got '{value}'");
            }
            else if (Repeatable(name)) result[name] = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            else result[name] = [value];
        }
        return result;
    }

    private static void ReadConfigFile(string text, string configPath, Dictionary<string, List<string>> raw)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            root = doc.RootElement.Clone();
        }
        catch (JsonException e) { throw new RelayUsageException($"config file '{configPath}' is not valid JSON: {e.Message}"); }
        if (root.ValueKind != JsonValueKind.Object) throw new RelayUsageException($"config file '{configPath}' must hold a JSON object");
        foreach (var property in root.EnumerateObject())
        {
            var name = property.Name;
            if (!IsKnown(name) && !Flags.Contains(name)) throw new RelayUsageException($"config file '{configPath}': unknown key '{name}'");
            if (name == "config") throw new RelayUsageException("config file: 'config' cannot be set inside a config file");
            if (Flags.Contains(name))
            {
                if (property.Value.ValueKind is JsonValueKind.True) raw[name] = ["true"];
                else if (property.Value.ValueKind is JsonValueKind.False) raw[name] = ["false"]; // overrides an environment variable that switched it on
                else throw new RelayUsageException($"config file: '{name}' must be true or false");
                continue;
            }
            IEnumerable<JsonElement> values = property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray() : [property.Value];
            foreach (var value in values)
            {
                var asText = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null,
                } ?? throw new RelayUsageException($"config file: '{name}' must be text or a number");
                Add(raw, name, asText);
            }
        }
    }

    private static string? FindOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i + 1 < args.Count; i++) if (args[i] == "--" + name) return args[i + 1];
        return null;
    }

    private static IEnumerable<string> CliKeys(IReadOnlyList<string> args) => args.Where(a => a.StartsWith("--", StringComparison.Ordinal) && a.Length > 2).Select(a => a[2..]);

    private static bool IsKnown(string name) => Texts.Contains(name) || Numbers.Any(n => n.Name == name);

    private static bool Repeatable(string name) => name is "public-name" or "trusted-proxy";

    private static void Add(Dictionary<string, List<string>> raw, string name, string value)
    {
        if (!raw.TryGetValue(name, out var list)) raw[name] = list = [];
        list.Add(value);
    }

    private static RelayOptions BindOptions(Dictionary<string, List<string>> raw)
    {
        string? Text(string name) => raw.TryGetValue(name, out var v) ? v[^1] : null;
        long? Number(string name)
        {
            if (!raw.TryGetValue(name, out var v)) return null;
            var (_, min, max) = Numbers.First(n => n.Name == name);
            if (!long.TryParse(v[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new RelayUsageException($"--{name} (or {EnvName(name)}) must be a whole number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}, got '{v[^1]}'");
            return value;
        }
        int Int(string name, int fallback) => (int?)Number(name) ?? fallback;
        long Long(string name, long fallback) => Number(name) ?? fallback;
        bool Flag(string name) => raw.ContainsKey(name);

        var defaults = new RelayLimits();
        var limits = new RelayLimits
        {
            MaxRooms = Int("max-rooms", defaults.MaxRooms),
            MaxSplices = Int("max-splices", defaults.MaxSplices),
            MaxControlPerNetwork = Int("max-control-per-network", defaults.MaxControlPerNetwork),
            MaxRoomsPerNetwork = Int("max-rooms-per-network", defaults.MaxRoomsPerNetwork),
            MaxConnectionsPerNetwork = Int("max-connections-per-network", defaults.MaxConnectionsPerNetwork),
            MaxJoinersPerRoom = Int("max-joiners-per-room", defaults.MaxJoinersPerRoom),
            MaxPendingPerRoom = Int("max-pending-per-room", defaults.MaxPendingPerRoom),
            MaxJoinersPerNetwork = Int("max-joiners-per-network", defaults.MaxJoinersPerNetwork),
            HandshakesPerMinute = Int("handshakes-per-minute", defaults.HandshakesPerMinute),
            JoinsPerMinute = Int("joins-per-minute", defaults.JoinsPerMinute),
            LookupFailuresPerMinute = Int("lookup-failures-per-minute", defaults.LookupFailuresPerMinute),
            BanMinutes = Int("ban-minutes", defaults.BanMinutes),
            ListingsPerMinute = Int("listings-per-minute", defaults.ListingsPerMinute),
            HttpRequestsPerMinute = Int("http-requests-per-minute", defaults.HttpRequestsPerMinute),
            SpliceRate = Long("splice-rate", defaults.SpliceRate),
            SpliceBurst = Long("splice-burst", defaults.SpliceBurst),
            RoomBytesPerDay = Long("room-bytes-per-day", defaults.RoomBytesPerDay),
            IdleSeconds = Int("idle-seconds", defaults.IdleSeconds),
            ControlSilenceSeconds = Int("control-silence-seconds", defaults.ControlSilenceSeconds),
            ListingStaleMinutes = Int("listing-stale-minutes", defaults.ListingStaleMinutes),
            RoomTtlHours = Int("room-ttl-hours", defaults.RoomTtlHours),
            HostTimeoutMillis = Int("host-timeout-millis", defaults.HostTimeoutMillis),
            PreambleTimeoutMillis = Int("preamble-timeout-millis", defaults.PreambleTimeoutMillis),
            FirstBytesSeconds = Int("first-bytes-seconds", defaults.FirstBytesSeconds),
            MaxMbps = Int("max-mbps", 0),
            MaxGibPerMonth = Int("max-gib-per-month", 0),
        };

        RelayTlsMode? mode = null;
        var tls = Text("tls");
        if (tls != null)
        {
            if (tls != "none") throw new RelayUsageException($"--tls must be 'none' (the default is the certificate or --self-signed), got '{tls}'");
            mode = RelayTlsMode.None;
        }
        var cert = Text("cert");
        var key = Text("key");
        var selfSigned = Flag("self-signed");
        if ((cert != null) != (key != null)) throw new RelayUsageException("--cert and --key go together");
        var chosen = (mode == RelayTlsMode.None ? 1 : 0) + (cert != null ? 1 : 0) + (selfSigned ? 1 : 0);
        if (chosen > 1) throw new RelayUsageException("choose one TLS mode: --cert/--key, --self-signed or --tls none");
        if (cert != null) mode = RelayTlsMode.Certificate;
        if (selfSigned) mode = RelayTlsMode.SelfSigned;

        var names = (raw.TryGetValue("public-name", out var n) ? n : []).Select(NormalizeName).Distinct(StringComparer.Ordinal).ToList();
        if (mode is RelayTlsMode.None or RelayTlsMode.SelfSigned && names.Count == 0)
            throw new RelayUsageException("--public-name is required with --tls none and --self-signed (hosts sign the relay name they dialled)");
        if (Flag("http-tls") && mode == RelayTlsMode.None) throw new RelayUsageException("--http-tls needs --cert or --self-signed");
        if (Flag("http-tls") && Flag("no-http")) throw new RelayUsageException("--http-tls and --no-http exclude each other");

        var proxies = new List<IPNetwork>();
        foreach (var text in raw.TryGetValue("trusted-proxy", out var t) ? t : [])
        {
            var network = default(IPNetwork);
            if (!IPNetwork.TryParse(text, out network) && !(IPAddress.TryParse(text, out var single) && IPNetwork.TryParse(text + (single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "/32" : "/128"), out network))) throw new RelayUsageException($"--trusted-proxy must be an address or CIDR, got '{text}'");
            proxies.Add(network);
        }

        var listing = Text("listing");
        var policy = listing switch
        {
            null or "open" => ListingPolicy.Open,
            "allowlist" => ListingPolicy.Allowlist,
            "off" => ListingPolicy.Off,
            _ => throw new RelayUsageException($"--listing must be open, allowlist or off, got '{listing}'"),
        };
        var level = Text("log-level") ?? "info";
        if (level is not ("debug" or "info" or "warn" or "error")) throw new RelayUsageException($"--log-level must be debug, info, warn or error, got '{level}'");
        foreach (var address in new[] { Text("bind"), Text("http-bind") })
            if (address != null && !IPAddress.TryParse(address, out _)) throw new RelayUsageException($"'{address}' is not an IP address");

        var defaultsOptions = new RelayOptions();
        return defaultsOptions with
        {
            DataDir = Text("data-dir") ?? defaultsOptions.DataDir,
            TunnelPort = Int("tunnel-port", DefaultTunnelPort),
            Bind = Text("bind"),
            TlsMode = mode,
            CertFile = cert,
            KeyFile = key,
            PublicNames = names,
            ProxyProtocol = Flag("proxy-protocol"),
            TrustedProxies = proxies.Count > 0 ? proxies : defaultsOptions.TrustedProxies,
            HttpPort = Int("http-port", DefaultHttpPort),
            HttpBind = Text("http-bind") ?? defaultsOptions.HttpBind,
            NoHttp = Flag("no-http"),
            HttpTls = Flag("http-tls"),
            Metrics = Flag("metrics"),
            Listing = policy,
            Name = Text("name") ?? defaultsOptions.Name,
            Contact = Text("contact") ?? "",
            Motd = Text("motd") ?? "",
            Limits = limits,
            LogLevel = level,
            LogRetentionDays = Int("log-retention-days", 7),
            NoLogFiles = Flag("no-log-files"),
            DrainSeconds = Int("drain-seconds", 10),
            SweepIntervalMillis = Int("sweep-interval-millis", 1000),
        };
    }

    /// <summary>The relay name as hosts sign it: lower case, no port; an IP literal in canonical form.</summary>
    public static string NormalizeName(string text)
    {
        var trimmed = text.Trim().Trim('[', ']');
        if (trimmed.Length == 0) throw new RelayUsageException("--public-name must not be empty");
        return IPAddress.TryParse(trimmed, out var ip) ? ip.ToString() : trimmed.ToLowerInvariant();
    }
}
