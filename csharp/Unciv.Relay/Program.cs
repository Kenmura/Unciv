using System.Runtime.InteropServices;

namespace Unciv.Relay;

/// <summary>T337: the relay executable. <c>Unciv.Relay --help</c> lists the options; the work is in <see cref="RelayRunner"/> and <see cref="RelayServer"/>.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        var reload = new ReloadSignal();
        // SIGINT and SIGTERM stop gracefully (the first one; a second falls through to the runtime's default so a stuck drain can be ended); SIGHUP reloads certificates and policy files.
        void OnStop(PosixSignalContext context)
        {
            context.Cancel = true;
            try { stop.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        using var sigint = TryRegister(PosixSignal.SIGINT, OnStop);
        using var sigterm = TryRegister(PosixSignal.SIGTERM, OnStop);
        using var sighup = TryRegister(PosixSignal.SIGHUP, context =>
        {
            context.Cancel = true;
            reload.Request();
        });
        return RelayRunner.RunAsync(args, Console.Out, Console.Error, stop.Token, reload).GetAwaiter().GetResult();
    }

    private static PosixSignalRegistration? TryRegister(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        try { return PosixSignalRegistration.Create(signal, handler); }
        catch (PlatformNotSupportedException) { return null; }
    }
}
