using System.Text.Json.Serialization;
using Unciv.Logic.Archaeology;

namespace Unciv.Logic;

// Brave New World (ticket B010, spec id B040, bnw-spec/SPEC.md section 2.2 and 4.4): the game-wide archaeology state. Both fields are omitted
// from the save while unused, so a game without archaeology (every G&K game) serialises exactly as before.
//
// Deliberate deviation from spec 2.2: there is no list of placed site records. A site is a tile with an Antiquity Site resource and its
// ArchaeologyRecord, so the tiles are the single source of truth.
public partial class GameInfo
{
    /// <summary>Have the sites been created (the first civ learned a tech that triggers them)? Happens once per game.</summary>
    [JsonPropertyName("archaeologySitesSpawned")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArchaeologySitesSpawned { get; set; }

    /// <summary>Finished digs waiting for their owner's choice, in completion order. Null while there are none.</summary>
    [JsonPropertyName("pendingDigs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PendingDig>? PendingDigs { get; set; }

    private void CloneArchaeologyTo(GameInfo target)
    {
        target.ArchaeologySitesSpawned = ArchaeologySitesSpawned;
        target.PendingDigs = PendingDigs?.Select(it => it.Clone()).ToList();
    }
}
