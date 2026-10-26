using System.Text.Json;
using Unciv.Commands;

namespace Unciv.Harness;

/// <summary>One captured step of a scenario: the PNG file name (without directory) and the caption shown on the review sheet.</summary>
public sealed record ScenarioCapture(string Step, string Caption);

/// <summary>
/// What a scenario run leaves in <c>out/&lt;scenario&gt;/result.json</c>: the commands the client submitted (the engine's own log), the
/// state hash it ended with, the captures for the review sheet and every failure. Round-trips through JSON so the review sheet and the
/// headless replay read exactly what the Godot run wrote.
/// </summary>
public sealed record ScenarioResult(
    string Scenario,
    long GameSeed,
    int MapSeed,
    string StateHash,
    IReadOnlyList<CommandEnvelope> Commands,
    IReadOnlyList<ScenarioCapture> Captures,
    IReadOnlyList<string> Checklist,
    IReadOnlyList<string> Failures,
    bool Solo = false,
    bool GodMode = false,
    bool Skirmish = false,
    bool Diplomacy = false,
    bool Rich = false,
    bool Policies = false,
    bool Alerts = false,
    bool Victory = false,
    bool Defeat = false,
    bool Trade = false,
    bool Decisions = false,
    bool Crowd = false)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public bool Passed => Failures.Count == 0;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ScenarioResult FromJson(string json) =>
        JsonSerializer.Deserialize<ScenarioResult>(json, Options) ?? throw new JsonException("Empty scenario result");
}
