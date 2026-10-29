using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using Unciv.Queries.Units;

namespace Unciv.Assets;

/// <summary>C091: a civilian and a military unit that travel together. <see cref="Target"/> is where the pair is headed (null while it has no standing move).</summary>
public sealed record EscortPair(int CivilianId, int MilitaryId, HexCoord? Target = null);

/// <summary>How one unit (or one escort pair, named by its military unit) fared in a group move.</summary>
public enum GroupMoveStatus
{
    Moved,
    Arrived,
    Blocked,
}

/// <summary>One line of a <see cref="GroupMoveReport"/>; <see cref="Reason"/> is the engine's refusal or the model's own words for a blocked unit.</summary>
public sealed record GroupMoveOutcome(int UnitId, GroupMoveStatus Status, string Reason = "");

/// <summary>What a group move did, one outcome per unit or pair in the order they were commanded.</summary>
public sealed record GroupMoveReport(IReadOnlyList<GroupMoveOutcome> Outcomes);

/// <summary>What <see cref="GroupMoveModel"/> needs from the screen: where units are now, what the engine says of a path, and the one ordinary move command.</summary>
public interface IGroupMoveEnvironment
{
    /// <summary>The current tile of one of the viewer's units, null when it is gone.</summary>
    HexCoord? Position(int unitId);

    float MovesLeft(int unitId);

    Task<PathPreviewResult?> PathAsync(int unitId, HexCoord to);

    /// <summary>Sends one move command (the unit's own move to a tile it can reach this turn).</summary>
    Task<SubmitResult> MoveAsync(int unitId, HexCoord to);
}

/// <summary>
/// C091: group move and escort as a client-side macro over ordinary commands. Nothing here changes the rules or the engine: every unit gets its own move command to the
/// first step of the engine's own path, asked right before the command so an earlier unit's move (1UPT) is already in the answer. A refused step is re-pathed once; a
/// target that cannot be entered (a group mate stands on it) falls back to the tiles around it, nearest first. The order of commands is a function of the distances and
/// ids, so the same selection and target always send the same commands.
/// </summary>
public static class GroupMoveModel
{
    /// <summary>The group after shift-clicking <paramref name="id"/>: added when absent, removed when present (ids keep the order they were added in).</summary>
    public static IReadOnlyList<int> Toggle(IReadOnlyList<int> group, int id) =>
        group.Contains(id) ? [.. group.Where(it => it != id)] : [.. group, id];

    /// <summary>
    /// The pair a two-unit group would make: exactly one civilian and one military unit standing no more than one tile apart. <paramref name="baseUnit"/> looks a unit
    /// up in the ruleset; a unit the ruleset does not know makes no pair.
    /// </summary>
    public static EscortPair? Offer(IReadOnlyList<UnitDto> group, Func<string, BaseUnit?> baseUnit)
    {
        if (group.Count != 2) return null;
        var first = baseUnit(group[0].Name);
        var second = baseUnit(group[1].Name);
        if (first == null || second == null || first.IsMilitary == second.IsMilitary) return null;
        if (HexMath.GetDistance(group[0].X, group[0].Y, group[1].X, group[1].Y) > 1) return null;
        var (civilian, military) = first.IsMilitary ? (group[1], group[0]) : (group[0], group[1]);
        return new EscortPair(civilian.Id, military.Id);
    }

    /// <summary>The menu entry that pairs the group (<paramref name="paired"/> false) or ends its escort.</summary>
    public static MenuEntry Entry(bool paired) => paired
        ? new MenuEntry(MenuEntryKind.Group, StopEscortLabel, true, null, StopEscortArgument, null, "Group:stop-escort", AssetKey.Of(RulesetAssetKeys.ActionIcon, "StopEscort"))
        : new MenuEntry(MenuEntryKind.Group, EscortLabel, true, null, EscortArgument, null, "Group:escort", AssetKey.Of(RulesetAssetKeys.ActionIcon, "Escort"));

    public const string EscortLabel = "Escort pair";
    public const string StopEscortLabel = "Stop escort";
    public const string EscortArgument = "escort";
    public const string StopEscortArgument = "stop-escort";

    /// <summary>The one-line description of a group, for the status bar.</summary>
    public static string Hint(int count, bool paired) => count < 2 ? "" : paired ? $"Escort pair selected: click a tile twice to move both" : $"Group of {count} selected: click a tile twice to move them";

    /// <summary>Moves every unit of <paramref name="ids"/> toward <paramref name="target"/>; units that are in a pair of <paramref name="pairs"/> (both of them selected) move as that pair.</summary>
    public static async Task<GroupMoveReport> MoveGroupAsync(IGroupMoveEnvironment env, IReadOnlyList<int> ids, IReadOnlyList<EscortPair> pairs, HexCoord target)
    {
        var chosen = pairs.Where(it => ids.Contains(it.CivilianId) && ids.Contains(it.MilitaryId)).ToList();
        var inPair = chosen.SelectMany(it => new[] { it.CivilianId, it.MilitaryId }).ToHashSet();
        var jobs = new List<(int Distance, int Id, Func<Task<GroupMoveOutcome>> Run)>();
        foreach (int id in ids.Where(it => !inPair.Contains(it)))
        {
            if (env.Position(id) is not { } at) continue;
            jobs.Add((HexMath.GetDistance(at, target), id, () => MoveOneAsync(env, id, target)));
        }
        foreach (var pair in chosen)
        {
            if (env.Position(pair.MilitaryId) is not { } military || env.Position(pair.CivilianId) is not { } civilian) continue;
            jobs.Add((Math.Min(HexMath.GetDistance(military, target), HexMath.GetDistance(civilian, target)), pair.MilitaryId, () => EscortStepAsync(env, pair, target)));
        }
        var outcomes = new List<GroupMoveOutcome>();
        foreach (var job in jobs.OrderBy(it => it.Distance).ThenBy(it => it.Id)) outcomes.Add(await job.Run());
        return new GroupMoveReport(outcomes);
    }

    /// <summary>One unit's move: the first step of the path to the target, re-pathed once when refused, then the same for the tiles around the target.</summary>
    public static async Task<GroupMoveOutcome> MoveOneAsync(IGroupMoveEnvironment env, int id, HexCoord target)
    {
        if (env.Position(id) is not { } at) return new GroupMoveOutcome(id, GroupMoveStatus.Blocked, "The unit is gone");
        if (at == target) return new GroupMoveOutcome(id, GroupMoveStatus.Arrived);
        if (env.MovesLeft(id) <= 0f) return new GroupMoveOutcome(id, GroupMoveStatus.Blocked, "No movement left");
        string reason = "Cannot reach that tile";
        bool targetReachable = false;
        foreach (var goal in Goals(at, target))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var path = await env.PathAsync(id, goal);
                if (path is not { Reachable: true, FirstStep: { } step }) break;
                if (goal == target) targetReachable = true;
                var result = await env.MoveAsync(id, new HexCoord(step.X, step.Y));
                if (result.Accepted) return new GroupMoveOutcome(id, GroupMoveStatus.Moved);
                reason = result.Message;
            }
            if (targetReachable && goal == target) break; // the engine finds a way but the step is refused twice: the tiles around the target would start with the same step
            if (goal == target && !targetReachable && HexMath.GetDistance(at, target) == 1)
                return new GroupMoveOutcome(id, GroupMoveStatus.Arrived); // next to a target a mate holds: that is as close as it gets
        }
        return new GroupMoveOutcome(id, GroupMoveStatus.Blocked, reason);
    }

    // The target first, then the tiles around it nearest to the unit (ties by coordinates).
    private static IEnumerable<HexCoord> Goals(HexCoord at, HexCoord target)
    {
        yield return target;
        foreach (var ring in HexMath.GetHexCoordsAtDistance(target, 1, 1, false).Where(it => it != at).OrderBy(it => HexMath.GetDistance(at, it)).ThenBy(it => it.x).ThenBy(it => it.y))
            yield return ring;
    }

    /// <summary>
    /// One turn of an escort: both units go to the same tile, the first step of either one's path that the other can also reach this turn (the military unit's first). The
    /// military unit moves first; the civilian then follows to wherever it really stopped (a move may end early), so the pair ends the turn on one tile whenever the
    /// engine lets it. When no common step exists neither unit moves.
    /// </summary>
    public static async Task<GroupMoveOutcome> EscortStepAsync(IGroupMoveEnvironment env, EscortPair pair, HexCoord target)
    {
        int lead = pair.MilitaryId;
        int follow = pair.CivilianId;
        if (env.Position(lead) is not { } leadAt || env.Position(follow) is not { } followAt) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, "The pair is broken");
        if (leadAt == target && followAt == target) return new GroupMoveOutcome(lead, GroupMoveStatus.Arrived);
        if (env.MovesLeft(lead) <= 0f || env.MovesLeft(follow) <= 0f) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, "No movement left");

        var leadPath = leadAt == target ? null : await env.PathAsync(lead, target);
        var followPath = followAt == target ? null : await env.PathAsync(follow, target);
        var steps = new List<HexCoord>();
        foreach (var (path, at) in new[] { (leadPath, leadAt), (followPath, followAt) })
        {
            // a first step that is the unit's own tile is no step (the engine has nothing to move it to)
            if (path is { Reachable: true, FirstStep: { } step } && new HexCoord(step.X, step.Y) is var tile && tile != at && !steps.Contains(tile)) steps.Add(tile);
        }
        if (steps.Count == 0) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, "Cannot reach that tile");

        HexCoord? common = null;
        foreach (var tile in steps)
        {
            if (await ReachesThisTurn(env, lead, leadAt, tile) && await ReachesThisTurn(env, follow, followAt, tile))
            {
                common = tile;
                break;
            }
        }
        if (common is not { } meeting)
        {
            // together on a tile next to a target a mate holds: as close as the pair gets
            if (leadAt == followAt && HexMath.GetDistance(leadAt, target) == 1) return new GroupMoveOutcome(lead, GroupMoveStatus.Arrived);
            return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, "The pair cannot keep together this turn");
        }

        if (leadAt != meeting)
        {
            var moved = await env.MoveAsync(lead, meeting);
            if (!moved.Accepted) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, moved.Message);
        }
        var gathered = env.Position(lead) is { } stopped && stopped != leadAt ? stopped : meeting; // unchanged: the command was accepted, so it is on the way (a remote snapshot may lag)
        if (env.Position(follow) is { } now && now != gathered)
        {
            var path = await env.PathAsync(follow, gathered);
            if (path is not { Reachable: true, FirstStep: { } step }) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, "The civilian cannot follow");
            var moved = await env.MoveAsync(follow, new HexCoord(step.X, step.Y));
            if (!moved.Accepted) return new GroupMoveOutcome(lead, GroupMoveStatus.Blocked, moved.Message);
        }
        return new GroupMoveOutcome(lead, GroupMoveStatus.Moved);
    }

    private static async Task<bool> ReachesThisTurn(IGroupMoveEnvironment env, int id, HexCoord at, HexCoord tile)
    {
        if (at == tile) return true;
        var path = await env.PathAsync(id, tile);
        return path is { Reachable: true, Turns: 1, FirstStep: { } step } && step.X == tile.x && step.Y == tile.y;
    }

    /// <summary>The words of a report for the status line: how many moved and who was blocked and why.</summary>
    public static string Summary(GroupMoveReport report, Func<int, string> nameOf)
    {
        int moved = report.Outcomes.Count(it => it.Status == GroupMoveStatus.Moved);
        int arrived = report.Outcomes.Count(it => it.Status == GroupMoveStatus.Arrived);
        var blocked = report.Outcomes.Where(it => it.Status == GroupMoveStatus.Blocked).ToList();
        var text = $"Group move: {moved} moved";
        if (arrived > 0) text += $", {arrived} already there";
        if (blocked.Count > 0) text += "; blocked: " + string.Join(", ", blocked.Select(it => $"{nameOf(it.UnitId)} ({it.Reason})"));
        return text;
    }
}
