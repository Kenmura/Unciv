using System.Text.RegularExpressions;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T374: the Docker deployment files must name the relay's real options. A renamed option, a typo in compose.yml or a forgotten line in .env.example fails here instead of
// being silently ignored on a server (the relay rejects an unknown flag, but an unknown environment variable would just do nothing).
public sealed partial class RelayDeployFilesTests
{
    /// <summary>Options that deliberately have no variable in compose.yml: the config path (use relay.json in the data volume) and the test-only sweep interval.</summary>
    private static readonly string[] NotInCompose = ["config", "sweep-interval-millis"];

    /// <summary>The container's fixed settings (Dockerfile ENV): compose publishes the ports, so changing them in .env would break the port mapping.</summary>
    private static readonly string[] FixedInDockerfile = ["data-dir", "bind", "http-bind", "tunnel-port", "http-port"];

    private static string Deploy => Path.Combine(RelayHostTests.RepoCsharpRoot(), "Unciv.Relay", "deploy");

    private static string Read(string name) => File.ReadAllText(Path.Combine(Deploy, name));

    private static string[] Expected(IEnumerable<string> excluding) =>
        [.. RelayOptions.OptionNames.Where(n => !excluding.Contains(n)).Select(RelayOptions.EnvName).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"^\s+(UNCIV_RELAY_[A-Z0-9_]+):\s*\$\{(UNCIV_RELAY_[A-Z0-9_]+):-\}\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeEnvLine();

    [GeneratedRegex(@"^(UNCIV_RELAY_[A-Z0-9_]+)=", RegexOptions.Multiline)]
    private static partial Regex EnvFileLine();

    [GeneratedRegex(@"\b(UNCIV_RELAY_[A-Z0-9_]+)=", RegexOptions.Multiline)]
    private static partial Regex DockerfileEnvLine();

    [Fact]
    public void ComposeEnvironmentNamesAreExactlyTheRelaysOptions()
    {
        var lines = ComposeEnvLine().Matches(Read("compose.yml"));
        Assert.All(lines, m => Assert.Equal(m.Groups[1].Value, m.Groups[2].Value)); // each variable passes the same-named variable of .env through
        var names = lines.Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Equal(Expected([.. NotInCompose, .. FixedInDockerfile]), names);
    }

    [Fact]
    public void EnvExampleDocumentsEveryComposeVariableOnce()
    {
        var names = EnvFileLine().Matches(Read(".env.example")).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Expected([.. NotInCompose, .. FixedInDockerfile]), names);
        Assert.Equal(names.Length, names.Distinct().Count());
        // the deploy-only variables compose.yml reads
        var env = Read(".env.example");
        foreach (var name in new[] { "RELAY_TUNNEL_PUBLISH", "RELAY_HTTP_PUBLISH", "RELAY_CERT_DIR" })
        {
            Assert.Contains(name + "=", env, StringComparison.Ordinal);
            Assert.Contains("${" + name + ":-", Read("compose.yml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DockerfileFixesExactlyTheContainerSettingsAndTheRelayDefaultPorts()
    {
        var docker = Read("Dockerfile");
        var fixedNames = DockerfileEnvLine().Matches(docker).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(Expected(RelayOptions.OptionNames.Where(n => !FixedInDockerfile.Contains(n))), fixedNames);
        Assert.Contains($"UNCIV_RELAY_TUNNEL_PORT={RelayOptions.DefaultTunnelPort}", docker, StringComparison.Ordinal);
        Assert.Contains($"UNCIV_RELAY_HTTP_PORT={RelayOptions.DefaultHttpPort}", docker, StringComparison.Ordinal);
        Assert.Contains($":{RelayOptions.DefaultTunnelPort}\"", Read("compose.yml"), StringComparison.Ordinal);
        Assert.Contains($":{RelayOptions.DefaultHttpPort}\"", Read("compose.yml"), StringComparison.Ordinal);
        foreach (var line in new[] { "USER relay", "HEALTHCHECK", "VOLUME /data", "AS build", "healthcheck.sh" }) Assert.Contains(line, docker, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShippedEnvExampleIsAValidRelayConfiguration()
    {
        // .env.example as shipped is TLS mode A (behind nginx): every non-empty variable must parse, and the result is the documented mode
        var env = Regex.Matches(Read(".env.example"), @"^(UNCIV_RELAY_[A-Z0-9_]+)=(.*)$", RegexOptions.Multiline, TimeSpan.FromSeconds(5)).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var container = new Dictionary<string, string> { ["UNCIV_RELAY_BIND"] = "0.0.0.0", ["UNCIV_RELAY_DATA_DIR"] = "/data" };
        foreach (var (key, value) in env) container[key] = value;
        var o = RelayOptions.ParseWithConfig([], _ => null, name => container.GetValueOrDefault(name));
        Assert.Equal(RelayTlsMode.None, o.TlsMode);
        Assert.True(o.ProxyProtocol);
        Assert.Equal(["relay.example.org"], o.PublicNames);
        Assert.Equal(3, o.TrustedProxies.Count);
        Assert.Equal(ListingPolicy.Open, o.Listing);
        Assert.Equal("0.0.0.0", o.Bind);
        Assert.Equal(new RelayLimits(), o.Limits); // every limit left empty = the design's default
    }

    [Fact]
    public void DeployFilesAreNotIgnoredByGit()
    {
        // T368: the deploy files were once covered by a global "deploy/" ignore rule and never committed. Ask git itself when it is available.
        var repo = RelayHostTests.RepoCsharpRoot();
        var files = new[] { "Dockerfile", "Dockerfile.dockerignore", "compose.yml", ".env.example", "healthcheck.sh", "unciv-relay.service", "certs/.gitkeep", "nginx/relay.conf.example", "nginx/relay-sni.conf.example", "nginx/relay-http.conf.example" };
        foreach (var file in files) Assert.True(File.Exists(Path.Combine(Deploy, file)), file);
        Assert.True(File.Exists(Path.Combine(repo, "Unciv.Relay", "SETUP.md")));
        if (!Directory.Exists(Path.Combine(repo, "..", ".git")) && !File.Exists(Path.Combine(repo, "..", ".git"))) return;
        var tracked = files.Select(f => Path.Combine("Unciv.Relay", "deploy", f)).Append(Path.Combine("Unciv.Relay", "SETUP.md"));
        var ignored = CheckIgnore(repo, tracked);
        if (ignored == null) return; // git is not installed
        Assert.Equal(1, ignored); // 1 = none of the paths is ignored
        Assert.Equal(0, CheckIgnore(repo, [Path.Combine("Unciv.Relay", "deploy", ".env")])); // but the real .env with the operator's settings is ignored
    }

    private static int? CheckIgnore(string repo, IEnumerable<string> paths)
    {
        try
        {
            using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", ["-C", repo, "check-ignore", "-q", .. paths]) { UseShellExecute = false });
            if (git == null) return null;
            git.WaitForExit(30_000);
            return git.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
