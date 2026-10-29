using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Mods;

namespace Unciv.Client.Net.Mods;

public enum ModSyncStatus { Missing, Different, Same }

/// <summary>One mod of a sync: what this client has of it, and how many bytes the download is.</summary>
public sealed record ModSyncItem(string Name, ModSyncStatus Status, long Bytes);

/// <summary>What a manifest means for this client: every mod the host plays with that is downloadable, and whether it is missing here, different, or already the same.</summary>
public sealed record ModSyncPlan(IReadOnlyList<ModSyncItem> Items)
{
    public IReadOnlyList<ModSyncItem> ToDownload => Items.Where(it => it.Status != ModSyncStatus.Same).ToList();

    public long TotalBytes => ToDownload.Sum(it => it.Bytes);
}

/// <summary>The end of a sync: <see cref="Code"/> is null on success, else a <see cref="ModSyncCodes"/> or <see cref="JoinRefusedCodes"/> value (never free text from the host).</summary>
public sealed record ModSyncOutcome(string? Code, ModManifest? Manifest, IReadOnlyList<string> Installed)
{
    public bool Success => Code == null;
}

/// <summary>Fetches one file named by the manifest from the host; throws <see cref="ModSyncException"/> when the host refuses or the connection fails.</summary>
public delegate Task<byte[]> ModFileFetcher(string mod, string path, CancellationToken cancellationToken);

/// <summary>Raised by a <see cref="ModFileFetcher"/> and by the install steps; <see cref="Code"/> is a <see cref="ModSyncCodes"/> value.</summary>
public sealed class ModSyncException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>What a <see cref="Session.ClientSession"/> calls when the host answers a mismatched join with a manifest.</summary>
public interface IModSyncHandler
{
    Task<ModSyncOutcome> SyncAsync(ModManifest manifest, ModFileFetcher fetch, CancellationToken cancellationToken);
}

/// <summary>
/// T247: the client side of the mod sync. Every byte from the host is untrusted: the manifest is validated before anything is requested (<see cref="ModManifestBuilder.Validate"/>), each file
/// is checked against its listed size and SHA-256 and must be strict UTF-8 text, the files are written under a hidden staging folder (names come from a fixed allow-list, never from the host),
/// the staged mod must load as a ruleset and rebuild to the manifest's checksum before it replaces anything, and a mod that was already installed is kept as <c>.sync-backup-NAME</c>.
/// Nothing is executed: a ruleset is data read by the json loader. The caller shows <see cref="Plan"/> through <c>confirm</c> first: nothing is downloaded without a yes.
/// </summary>
public sealed class ModSyncService(IRulesetFileSource files, string modsDirectory, Func<ModSyncPlan, Task<bool>> confirm) : IModSyncHandler
{
    private const string StagingPrefix = ".sync-staging-";
    private const string BackupPrefix = ".sync-backup-";

    /// <summary>What a manifest needs from this client. Fails with <see cref="ModSyncException"/> for a manifest that is not acceptable or a name that collides with an installed mod of another spelling.</summary>
    public ModSyncPlan Plan(ModManifest manifest)
    {
        if (ModManifestBuilder.Validate(manifest) != null) throw new ModSyncException(ModSyncCodes.BadManifest, "manifest rejected");
        var installed = files.ModsFolder.List().Where(it => it.IsDirectory).Select(it => it.Name).ToList();
        var items = new List<ModSyncItem>();
        foreach (var entry in manifest.Entries)
        {
            if (installed.Any(it => string.Equals(it, entry.Name, StringComparison.OrdinalIgnoreCase) && !string.Equals(it, entry.Name, StringComparison.Ordinal)))
                throw new ModSyncException(ModSyncCodes.Failed, "a mod with a similar name is installed");
            var local = files.ModsFolder.Child(entry.Name);
            var status = !local.Exists || !local.IsDirectory ? ModSyncStatus.Missing
                : ModManifestBuilder.Build(entry.Name, local)?.Checksum == entry.Checksum ? ModSyncStatus.Same
                : ModSyncStatus.Different;
            items.Add(new ModSyncItem(entry.Name, status, entry.Files.Sum(it => it.Size)));
        }
        return new ModSyncPlan(items);
    }

    public async Task<ModSyncOutcome> SyncAsync(ModManifest manifest, ModFileFetcher fetch, CancellationToken cancellationToken)
    {
        ModSyncPlan plan;
        try { plan = Plan(manifest); }
        catch (ModSyncException e) { return new ModSyncOutcome(e.Code, null, []); }

        var needed = plan.ToDownload;
        if (needed.Count == 0) return new ModSyncOutcome(null, manifest, []);
        bool yes;
        try { yes = await confirm(plan).ConfigureAwait(false); }
        catch (Exception e) when (e is not OperationCanceledException) { yes = false; } // a failing prompt is a "no"
        if (!yes) return new ModSyncOutcome(ModSyncCodes.Declined, null, []);

        Directory.CreateDirectory(modsDirectory);
        var staging = Path.Combine(modsDirectory, StagingPrefix + StagingSuffix());
        try
        {
            foreach (var item in needed)
                await StageAsync(manifest.Entries.First(it => it.Name == item.Name), staging, fetch, cancellationToken).ConfigureAwait(false);
            var installed = Swap(needed.Select(it => it.Name).ToList(), staging);
            return new ModSyncOutcome(null, manifest, installed);
        }
        catch (ModSyncException e) { return new ModSyncOutcome(e.Code, null, []); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new ModSyncOutcome(ModSyncCodes.Failed, null, []); }
        finally { TryDelete(staging); }
    }

    private async Task StageAsync(ModEntry entry, string staging, ModFileFetcher fetch, CancellationToken cancellationToken)
    {
        var modRoot = Path.Combine(staging, entry.Name);
        var jsons = Path.Combine(modRoot, "jsons");
        Directory.CreateDirectory(jsons);
        foreach (var file in entry.Files)
        {
            var bytes = await fetch(entry.Name, file.Path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length != file.Size || !string.Equals(ModManifestBuilder.HashOf(bytes), file.Sha256, StringComparison.Ordinal) || ModSyncRules.TryDecode(bytes) == null)
                throw new ModSyncException(ModSyncCodes.BadFile, "file does not match the manifest");
            var target = Path.GetFullPath(Path.Combine(jsons, file.Path));
            if (!string.Equals(Path.GetDirectoryName(target), Path.GetFullPath(jsons), StringComparison.Ordinal)) throw new ModSyncException(ModSyncCodes.BadFile, "path escapes the staging folder");
            await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
        }

        // The staged copy must be exactly the manifest's mod, and must load as a ruleset, before it may replace anything.
        var staged = new DirectoryRulesetFolder(modRoot);
        if (ModManifestBuilder.Build(entry.Name, staged)?.Checksum != entry.Checksum) throw new ModSyncException(ModSyncCodes.BadFile, "staged mod differs from the manifest");
        try
        {
            new Ruleset { Name = entry.Name }.Load(staged.Child("jsons"), files.GlobalJsonsFolder);
        }
        catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new ModSyncException(ModSyncCodes.BadFile, "mod does not load");
        }
    }

    /// <summary>Moves the staged mods into the mods folder, each existing one aside to its backup first; undoes everything when a step fails.</summary>
    private List<string> Swap(List<string> names, string staging)
    {
        var done = new List<(string Name, bool HadOld)>();
        try
        {
            foreach (var name in names)
            {
                var target = Path.Combine(modsDirectory, name);
                var backup = Path.Combine(modsDirectory, BackupPrefix + name);
                var hadOld = Directory.Exists(target);
                if (hadOld)
                {
                    if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
                    Directory.Move(target, backup);
                }
                done.Add((name, hadOld));
                Directory.Move(Path.Combine(staging, name), target);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var (name, hadOld) in done.AsEnumerable().Reverse())
            {
                var target = Path.Combine(modsDirectory, name);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                if (hadOld) Directory.Move(Path.Combine(modsDirectory, BackupPrefix + name), target);
            }
            throw;
        }
        return names;
    }

    private static string StagingSuffix()
    {
        Span<byte> bytes = stackalloc byte[6];
        SystemSecureRandom.Instance.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private static void TryDelete(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a leftover hidden staging folder is harmless: the loader skips dot folders */ }
    }

    /// <summary>
    /// Makes the installed mods live without a restart: reloads each into <see cref="RulesetCache"/> and checks that the combined ruleset now hashes like the host's. Call it on the thread that owns
    /// the cache (the UI thread), after <see cref="ModSyncCodes.Synced"/>. Returns null when the client now plays the host's rules, else the code to show.
    /// </summary>
    public string? HotLoad(ModSyncOutcome outcome)
    {
        if (outcome.Manifest is not { } manifest) return ModSyncCodes.Failed;
        try
        {
            foreach (var name in outcome.Installed)
                if (RulesetCache.ReloadSingleRuleset(files, name).Count > 0) return ModSyncCodes.BadFile;
            var hash = new LobbySettings { Ruleset = manifest.BaseRuleset, Mods = [.. manifest.Mods] }.ComputeRulesetHash();
            return string.Equals(hash, manifest.RulesetHash, StringComparison.OrdinalIgnoreCase) ? null : JoinRefusedCodes.RulesetMismatch;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return ModSyncCodes.Failed;
        }
    }
}
