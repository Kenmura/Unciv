using System.Globalization;
using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>A SIGHUP in waiting: the signal handler calls <see cref="Request"/>, the runner reloads certificates and policy files.</summary>
public sealed class ReloadSignal
{
    private readonly SemaphoreSlim signal = new(0);

    public void Request() => signal.Release();

    public Task WaitAsync(CancellationToken cancellationToken) => signal.WaitAsync(cancellationToken);
}

/// <summary>What <see cref="Program.Main"/> does, testable in-process: parse, start, print the bound ports, run until the stop token fires, stop gracefully.</summary>
public static class RelayRunner
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken stop, ReloadSignal? reload = null, IClock? clock = null)
    {
        RelayOptions options;
        try
        {
            options = RelayOptions.ParseWithConfig(args, path => File.Exists(path) ? File.ReadAllText(path) : null, Environment.GetEnvironmentVariable);
        }
        catch (RelayUsageException e)
        {
            await error.WriteLineAsync("Unciv.Relay: " + e.Message).ConfigureAwait(false);
            return 2;
        }
        if (options.Help)
        {
            await output.WriteLineAsync(RelayOptions.HelpText).ConfigureAwait(false);
            return 0;
        }

        var server = new RelayServer(options, clock, output);
        await using (server.ConfigureAwait(false))
        {
            try { server.Start(); }
            catch (RelayUsageException e)
            {
                await error.WriteLineAsync("Unciv.Relay: " + e.Message).ConfigureAwait(false);
                return 2;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                await error.WriteLineAsync("Unciv.Relay: " + e.Message).ConfigureAwait(false);
                return 1;
            }
            await output.WriteLineAsync("LISTENING tunnel " + server.TunnelPort.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (server.HttpPort != 0) await output.WriteLineAsync("LISTENING http " + server.HttpPort.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (server.CertificateFingerprint != null) await output.WriteLineAsync("RELAY-FINGERPRINT " + server.CertificateFingerprint).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    if (reload == null) await Task.Delay(Timeout.Infinite, stop).ConfigureAwait(false);
                    else
                    {
                        await reload.WaitAsync(stop).ConfigureAwait(false);
                        await server.ReloadAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { /* the stop request */ }
            await server.StopAsync().ConfigureAwait(false);
        }
        return 0;
    }
}
