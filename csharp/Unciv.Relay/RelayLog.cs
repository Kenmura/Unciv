using System.Globalization;
using Unciv.Net.Turns;

namespace Unciv.Relay;

/// <summary>
/// One structured line per event (RELAY.md section 10): <c>time LEVEL event room= fp= peer= reason= bytes= ip=</c>. Never a splice byte, a preamble body, a name or a password:
/// callers can only pass the fields below. Goes to stdout and to <c>logs/relay-YYYY-MM-DD.log</c>; files older than the retention are deleted by <see cref="Rotate"/>.
/// </summary>
public sealed class RelayLog(TextWriter? output, string? logDirectory, int retentionDays, string minimumLevel, IClock clock)
{
    private static readonly string[] Levels = ["debug", "info", "warn", "error"];
    private readonly Lock gate = new();
    private readonly int minimum = Math.Max(0, Array.IndexOf(Levels, minimumLevel));

    public void Debug(string ev, string? room = null, string? fp = null, string? peer = null, string? reason = null, long? bytes = null, string? ip = null) =>
        Write(0, ev, room, fp, peer, reason, bytes, ip);

    public void Info(string ev, string? room = null, string? fp = null, string? peer = null, string? reason = null, long? bytes = null, string? ip = null) =>
        Write(1, ev, room, fp, peer, reason, bytes, ip);

    public void Warn(string ev, string? room = null, string? fp = null, string? peer = null, string? reason = null, long? bytes = null, string? ip = null) =>
        Write(2, ev, room, fp, peer, reason, bytes, ip);

    public void Error(string ev, string? reason = null) => Write(3, ev, null, null, null, reason, null, null);

    private static string Clean(string value) => new string(value.Select(c => char.IsWhiteSpace(c) || char.IsControl(c) ? '_' : c).ToArray()).Replace("=", "-", StringComparison.Ordinal);

    private void Write(int level, string ev, string? room, string? fp, string? peer, string? reason, long? bytes, string? ip)
    {
        if (level < minimum) return;
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMillis);
        var line = $"{now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)} {Levels[level].ToUpperInvariant(),-5} {Clean(ev)}";
        if (room != null) line += " room=" + Clean(room);
        if (fp != null) line += " fp=" + Clean(fp.Length > 8 ? fp[..8] : fp);
        if (peer != null) line += " peer=" + Clean(peer);
        if (reason != null) line += " reason=" + Clean(reason);
        if (bytes != null) line += " bytes=" + bytes.Value.ToString(CultureInfo.InvariantCulture);
        if (ip != null) line += " ip=" + Clean(ip);
        lock (gate)
        {
            output?.WriteLine(line);
            output?.Flush();
            if (logDirectory == null) return;
            try
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, FileName(now)), line + "\n");
            }
            catch (IOException) { /* a full disk must not take the relay down */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string FileName(DateTimeOffset day) => "relay-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";

    /// <summary>Deletes log files older than the retention (by the date in the file name, against the injected clock).</summary>
    public int Rotate()
    {
        if (logDirectory == null || !Directory.Exists(logDirectory)) return 0;
        var today = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMillis).UtcDateTime.Date;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(logDirectory, "relay-*.log"))
        {
            var stem = Path.GetFileNameWithoutExtension(file)["relay-".Length..];
            if (!DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || (today - day).TotalDays < retentionDays) continue;
            try { File.Delete(file); removed++; }
            catch (IOException) { }
        }
        return removed;
    }
}
