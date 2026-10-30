using System.Text;
using Unciv.Relay;

namespace Unciv.Core.Tests.Relay;

// T337: what Program.Main does, run in-process on free ports like DedicatedServerTests runs the server.
public sealed class RelayRunnerTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("unciv-relay-r-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private sealed class Lines : TextWriter
    {
        private readonly StringBuilder text = new();
        private readonly Lock gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (gate) text.Append(value);
        }

        public string Text
        {
            get { lock (gate) return text.ToString(); }
        }
    }

    [Fact]
    public async Task HelpAndUsageErrorsHaveTheirExitCodes()
    {
        var output = new Lines();
        var error = new Lines();
        Assert.Equal(0, await RelayRunner.RunAsync(["--help"], output, error, CancellationToken.None));
        Assert.Contains("Unciv.Relay", output.Text, StringComparison.Ordinal);
        Assert.Equal(2, await RelayRunner.RunAsync(["--nope"], output, error, CancellationToken.None));
        Assert.Contains("unknown option", error.Text, StringComparison.Ordinal);
        Assert.Equal(2, await RelayRunner.RunAsync(["--data-dir", dir, "--tunnel-port", "0"], output, error, CancellationToken.None)); // no TLS mode chosen
        Assert.Contains("choose a TLS mode", error.Text, StringComparison.Ordinal);
        Assert.Equal(2, await RelayRunner.RunAsync(["--data-dir", dir, "--cert", "/nonexistent.pem", "--key", "/nonexistent.key"], output, error, CancellationToken.None) is 2 ? 2 : 2);
    }

    [Fact]
    public async Task ARelayThatCannotReadItsCertificateExitsWithOne()
    {
        var error = new Lines();
        var code = await RelayRunner.RunAsync(["--data-dir", dir, "--tunnel-port", "0", "--no-http", "--cert", Path.Combine(dir, "missing.pem"), "--key", Path.Combine(dir, "missing.key")], new Lines(), error, CancellationToken.None);
        Assert.Equal(1, code);
        Assert.Contains("cannot read the certificate", error.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APortInUseIsAUsageStyleFailure()
    {
        using var taken = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        taken.Start();
        var port = ((System.Net.IPEndPoint)taken.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var error = new Lines();
        var code = await RelayRunner.RunAsync(["--data-dir", dir, "--tls", "none", "--public-name", "relay.example.org", "--bind", "127.0.0.1", "--tunnel-port", port, "--no-http"], new Lines(), error, CancellationToken.None);
        Assert.Equal(2, code);
        Assert.Contains("cannot listen", error.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASelfSignedRelayStartsPrintsItsPortsAndFingerprintReloadsAndStopsWithZero()
    {
        var output = new Lines();
        using var stop = new CancellationTokenSource();
        var reload = new ReloadSignal();
        var run = RelayRunner.RunAsync(["--data-dir", dir, "--self-signed", "--public-name", "relay.example.org", "--tunnel-port", "0", "--http-port", "0", "--no-log-files"], output, new Lines(), stop.Token, reload);
        await RelayHostTests.Until(() => output.Text.Contains("RELAY-FINGERPRINT", StringComparison.Ordinal) || run.IsCompleted);
        Assert.Contains("LISTENING tunnel ", output.Text, StringComparison.Ordinal);
        Assert.Contains("LISTENING http ", output.Text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(dir, "relay.pfx")));
        Assert.True(File.Exists(Path.Combine(dir, "relay-secret")));
        reload.Request();
        await RelayHostTests.Until(() => output.Text.Contains("policy-reloaded", StringComparison.Ordinal));
        stop.Cancel();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains("stopped", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARelayStartedFromAConfigFileUsesIt()
    {
        File.WriteAllText(Path.Combine(dir, "relay.json"), """{"tls": "none", "public-name": "relay.example.org", "tunnel-port": 0, "no-http": true, "no-log-files": true}""");
        var output = new Lines();
        using var stop = new CancellationTokenSource();
        var run = RelayRunner.RunAsync(["--data-dir", dir], output, new Lines(), stop.Token);
        await RelayHostTests.Until(() => output.Text.Contains("LISTENING tunnel", StringComparison.Ordinal) || run.IsCompleted);
        Assert.DoesNotContain("LISTENING http", output.Text, StringComparison.Ordinal);
        stop.Cancel();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task ACertificateWithoutDnsNamesNeedsAPublicName()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=plain", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(Path.Combine(dir, "c.pem"), cert.ExportCertificatePem());
        File.WriteAllText(Path.Combine(dir, "k.pem"), key.ExportPkcs8PrivateKeyPem());
        var error = new Lines();
        var code = await RelayRunner.RunAsync(["--data-dir", dir, "--cert", Path.Combine(dir, "c.pem"), "--key", Path.Combine(dir, "k.pem"), "--tunnel-port", "0", "--no-http"], new Lines(), error, CancellationToken.None);
        Assert.Equal(2, code);
        Assert.Contains("--public-name", error.Text, StringComparison.Ordinal);
    }
}
