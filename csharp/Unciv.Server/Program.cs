using System.Runtime.InteropServices;

namespace Unciv.Server;

/// <summary>T265: the dedicated server. <c>Unciv.Server --help</c> lists the options; the work is in <see cref="ServerRunner"/>.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        // T270: SIGINT and SIGTERM (Ctrl+C on Windows) ask for a clean stop: the runner saves the game and exits 0. The first signal is swallowed so the save can finish;
        // a second one is left to the runtime's default (the process ends at once), the way out of a save that hangs.
        var signalled = 0;
        void OnSignal(PosixSignalContext context)
        {
            if (Interlocked.Exchange(ref signalled, 1) != 0) return;
            context.Cancel = true;
            try { stop.Cancel(); }
            catch (ObjectDisposedException) { } // the run already ended
        }
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var sigterm = TryRegister(PosixSignal.SIGTERM, OnSignal); // not available on every platform (Windows delivers only Ctrl+C)
        return ServerRunner.RunAsync(args, Console.Out, Console.Error, stop.Token).GetAwaiter().GetResult();
    }

    private static PosixSignalRegistration? TryRegister(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        try { return PosixSignalRegistration.Create(signal, handler); }
        catch (PlatformNotSupportedException) { return null; }
    }
}
