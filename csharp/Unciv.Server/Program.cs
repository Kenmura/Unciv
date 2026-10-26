namespace Unciv.Server;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine("Unciv.Server - Headless Authoritative Game Host");
            Console.WriteLine("Usage: dotnet run --project Unciv.Server [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h       Show help");
            Console.WriteLine("  --game-id <id>   Load or create game with specified ID");
            Console.WriteLine("  --data-dir <dir> Set data directory");
            return 0;
        }

        Console.WriteLine("Unciv.Server headless host initialized.");
        return 0;
    }
}
