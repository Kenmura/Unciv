using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace Unciv.Relay;

public sealed partial class RelayServer
{
    /// <summary>An answer of the read-only directory: an HTTP-style status and the JSON it carries (null for an error without a body).</summary>
    internal sealed record DirectoryReply(int Status, JsonNode? Body);

    /// <summary>Serves <c>/v1/info</c>, <c>/v1/games[?filters]</c> and <c>/v1/games/{room}</c> (RELAY.md section 7). Everything it returns comes from signed listings; it never reveals an address.</summary>
    internal DirectoryReply ServeDirectory(string pathAndQuery)
    {
        var question = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? pathAndQuery : pathAndQuery[..question];
        var query = HttpUtility.ParseQueryString(question < 0 ? "" : pathAndQuery[(question + 1)..]);
        if (path == "/v1/info") return new DirectoryReply(200, Info());
        if (path == "/v1/games") return options.Listing == ListingPolicy.Off ? new DirectoryReply(404, null) : new DirectoryReply(200, Games(query));
        if (path.StartsWith("/v1/games/", StringComparison.Ordinal))
        {
            var code = RelayProtocol.NormalizeRoom(path["/v1/games/".Length..]);
            lock (gate)
            {
                var listing = code == null ? null : rooms.GetValueOrDefault(code);
                return listing?.Listing == null ? new DirectoryReply(404, null) : new DirectoryReply(200, Entry(listing, listing.Listing));
            }
        }
        return new DirectoryReply(404, null);
    }

    private JsonObject Info()
    {
        int live;
        int listed;
        lock (gate)
        {
            live = rooms.Values.Count(r => r.Live);
            listed = rooms.Values.Count(r => r.Listing != null);
        }
        return new JsonObject
        {
            ["name"] = options.Name,
            ["version"] = "1",
            ["proto"] = RelayProtocol.Version,
            ["listing"] = options.Listing.ToString().ToLowerInvariant(),
            ["contact"] = options.Contact,
            ["motd"] = options.Motd,
            ["limits"] = new JsonObject
            {
                ["maxRooms"] = limits.MaxRooms,
                ["maxJoinersPerRoom"] = limits.MaxJoinersPerRoom,
                ["spliceRate"] = limits.SpliceRate,
                ["spliceBurst"] = limits.SpliceBurst,
                ["roomBytesPerDay"] = limits.RoomBytesPerDay,
                ["idleSeconds"] = limits.IdleSeconds,
            },
            ["rooms"] = live,
            ["listed"] = listed,
        };
    }

    private JsonObject Entry(Room room, Listing listing) => new()
    {
        ["room"] = room.Display,
        ["fp"] = room.FingerprintHex,
        ["body"] = listing.Body,
        ["sig"] = listing.Signature,
        ["cert"] = Convert.ToBase64String(room.Certificate),
        ["ageSeconds"] = (clock.NowMillis - listing.FirstSeen) / 1000,
    };

    private JsonObject Games(System.Collections.Specialized.NameValueCollection query)
    {
        var q = query["q"];
        var ruleset = query["ruleset"];
        var mod = query["mod"];
        var version = query["version"];
        var hasSlots = query["hasSlots"] == "1";
        var password = query["passwordRequired"] is "0" or "1" ? query["passwordRequired"] == "1" : (bool?)null;
        var started = query["started"] is "0" or "1" ? query["started"] == "1" : (bool?)null;
        var limit = int.TryParse(query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out var asked) && asked is >= 1 and <= 100 ? asked : 100;
        long cursorTime = long.MaxValue;
        var cursorRoom = "";
        var cursor = query["cursor"];
        if (cursor != null)
        {
            var dot = cursor.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && long.TryParse(cursor[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var time)) { cursorTime = time; cursorRoom = cursor[(dot + 1)..]; }
        }
        List<(Room Room, Listing Listing)> matches;
        lock (gate)
        {
            matches = rooms.Values.Where(r => r.Live && r.Listing != null).Select(r => (Room: r, Listing: r.Listing!))
                .Where(x => (x.Listing.FirstSeen < cursorTime || (x.Listing.FirstSeen == cursorTime && string.CompareOrdinal(x.Room.Code, cursorRoom) > 0))
                    && (q == null || x.Listing.Fields.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Listing.Fields.HostName.Contains(q, StringComparison.OrdinalIgnoreCase))
                    && (ruleset == null || x.Listing.Fields.Ruleset == ruleset)
                    && (mod == null || x.Listing.Fields.Mods.Contains(mod))
                    && (version == null || x.Listing.Fields.Version == version)
                    && (!hasSlots || x.Listing.Fields.Players < x.Listing.Fields.Slots)
                    && (password == null || x.Listing.Fields.PasswordRequired == password)
                    && (started == null || x.Listing.Fields.Started == started))
                .OrderByDescending(x => x.Listing.FirstSeen).ThenBy(x => x.Room.Code, StringComparer.Ordinal).ToList();
        }
        var page = new JsonArray();
        foreach (var (room, listing) in matches.Take(limit)) page.Add(Entry(room, listing));
        var last = matches.Count > limit ? matches[limit - 1] : default;
        return new JsonObject
        {
            ["games"] = page,
            ["next"] = matches.Count > limit ? last.Listing.FirstSeen.ToString(CultureInfo.InvariantCulture) + "." + last.Room.Code : null,
        };
    }

    private async Task HandleGetAsync(RelayConn conn, JsonObject p)
    {
        var path = Text(p, "path");
        if (path == null || !path.StartsWith('/') || path.Length > 1024)
        {
            await conn.TrySendAsync(Error("bad-request")).ConfigureAwait(false);
            return;
        }
        var reply = ServeDirectory(path);
        if (reply.Body == null)
        {
            await conn.TrySendAsync(Error("not-found", ("status", reply.Status))).ConfigureAwait(false);
            return;
        }
        var message = new JsonObject { ["ok"] = true, ["status"] = reply.Status, ["data"] = reply.Body };
        if (Encoding.UTF8.GetByteCount(message.ToJsonString()) > RelayProtocol.GetReplyMaxBytes - 1)
        {
            await conn.TrySendAsync(Error("too-large")).ConfigureAwait(false);
            return;
        }
        await conn.TrySendAsync(message).ConfigureAwait(false);
    }

    /// <summary>The Prometheus text of <c>GET /metrics</c>: counts only.</summary>
    public string Metrics()
    {
        var text = new StringBuilder();
        void Add(string name, string help, long value) =>
            text.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n').Append("# TYPE ").Append(name).Append(" gauge\n").Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        Add("unciv_relay_rooms", "Rooms with a live host.", RoomCount);
        Add("unciv_relay_splices", "Running splices.", SpliceCount);
        Add("unciv_relay_listed", "Listed games.", ListedCount);
        Add("unciv_relay_joins_total", "Joins spliced since start.", Interlocked.Read(ref joinsTotal));
        Add("unciv_relay_bytes_total", "Bytes spliced by finished splices since start.", Interlocked.Read(ref bytesTotal));
        return text.ToString();
    }
}
