namespace Unciv.Server;

/// <summary>T265: the dedicated server. <c>Unciv.Server --help</c> lists the options; the work is in <see cref="ServerRunner"/>.</summary>
public static class Program
{
    public static int Main(string[] args) => ServerRunner.RunAsync(args, Console.Out, Console.Error, CancellationToken.None).GetAwaiter().GetResult();
}
