using System.Globalization;
using Unciv.Queries.Victory;

namespace Unciv.Assets;

public enum GameOverButtonKind
{
    /// <summary>Send <c>OneMoreTurn</c> and go on playing past the victory.</summary>
    OneMoreTurn,

    /// <summary>Dismiss the defeat screen and look at the world as a spectator.</summary>
    KeepWatching,

    /// <summary>Leave the game for the main menu.</summary>
    MainMenu,
}

public sealed record GameOverButton(string Id, GameOverButtonKind Kind, string Label);

/// <summary><see cref="Place"/> is 0 for a defeated civ (it is listed last without a place).</summary>
public sealed record GameOverRankRow(int Place, string Civ, bool IsViewer, bool IsDefeated, int Value);

/// <summary>One civ's line of the history graph: points in the unit square, x = turn left to right, y = 0 at the top (the highest value).</summary>
public sealed record GraphLine(string Civ, bool IsViewer, bool IsDefeated, IReadOnlyList<GraphPoint> Points);

public readonly record struct GraphPoint(float X, float Y);

/// <summary>The graph's axes in the engine's units: first and last turn, lowest and highest value.</summary>
public sealed record GraphAxes(int FirstTurn, int LastTurn, int MinValue, int MaxValue, IReadOnlyList<GraphLine> Lines);

public sealed record GameOverView(string Status, string Title, string Text, IReadOnlyList<GameOverButton> Buttons, IReadOnlyList<GameOverRankRow> Ranking, GraphAxes Graph);

/// <summary>
/// C024: the victory and defeat screen as plain data, Godot-free so it is unit-tested. The outcome comes from the engine's <see cref="GameOverResult"/>, the ranking
/// from <see cref="RankingsQuery"/> and the graph from <see cref="HistoryQuery"/> (both viewer-filtered by the engine; after a victory they list everyone, as Kotlin does).
/// This class words the title and the buttons, numbers the places and scales the graph; it decides nothing about the game. Titles are translation keys in the Kotlin
/// form with the names in square brackets, so the client translates them with <c>Tr()</c>.
/// </summary>
public static class GameOverModel
{
    public const string UnknownCiv = "???";

    public static bool IsShown(GameOverResult result) => result.Status != GameOverQuery.Playing;

    public static GameOverView Build(GameOverResult result, IReadOnlyList<RankingColumn>? rankings, HistoryResult? history)
    {
        string status = result.Status;
        string title = status switch
        {
            GameOverQuery.Won => $"You have won a [{result.VictoryType}] Victory!",
            GameOverQuery.Lost => $"[{result.Winner}] has won a [{result.VictoryType}] Victory!",
            GameOverQuery.Defeated => "Defeated",
            _ => "",
        };
        var buttons = new List<GameOverButton>();
        if (status is GameOverQuery.Won or GameOverQuery.Lost) buttons.Add(new GameOverButton("OneMoreTurn", GameOverButtonKind.OneMoreTurn, "One more turn...!"));
        if (status == GameOverQuery.Defeated) buttons.Add(new GameOverButton("KeepWatching", GameOverButtonKind.KeepWatching, "Keep watching"));
        buttons.Add(new GameOverButton("MainMenu", GameOverButtonKind.MainMenu, "Main menu"));
        return new GameOverView(status, title, result.Text, buttons, Ranking(rankings), Graph(history));
    }

    /// <summary>
    /// C044: the screen after the viewer resigned. Not an engine status (the game goes on with the AI in the viewer's place), so it is built here from the same ranking and history and offers only the way out.
    /// </summary>
    public static GameOverView BuildResigned(IReadOnlyList<RankingColumn>? rankings, HistoryResult? history) =>
        new("Resigned", GameMenuModel.ResignedTitle, GameMenuModel.ResignedText, [new GameOverButton("MainMenu", GameOverButtonKind.MainMenu, "Main menu")], Ranking(rankings), Graph(history));

    /// <summary>The Score column when there is one, else the first; empty without any. Equal scores share a place; defeated civs come last with no place.</summary>
    public static IReadOnlyList<GameOverRankRow> Ranking(IReadOnlyList<RankingColumn>? columns)
    {
        var column = columns?.FirstOrDefault(it => it.Type == "Score") ?? columns?.FirstOrDefault();
        if (column == null) return [];
        var alive = column.Entries.Where(it => !it.Civ.IsDefeated).ToList();
        var rows = new List<GameOverRankRow>();
        foreach (var entry in alive)
            rows.Add(new GameOverRankRow(1 + alive.Count(it => it.Value > entry.Value), Name(entry.Civ), entry.Civ.IsViewer, false, entry.Value));
        foreach (var entry in column.Entries.Where(it => it.Civ.IsDefeated))
            rows.Add(new GameOverRankRow(0, Name(entry.Civ), entry.Civ.IsViewer, true, entry.Value));
        return rows;
    }

    /// <summary>
    /// Scales every series into the unit square over the turns and values present. A single turn sits at x 0.5 and a flat value at y 0.5, so a one-point line is still drawn.
    /// Without any point the axes are all zero and there are no lines.
    /// </summary>
    public static GraphAxes Graph(HistoryResult? history)
    {
        var series = history?.Series.Where(it => it.Points.Count > 0).ToList() ?? [];
        if (series.Count == 0) return new GraphAxes(0, 0, 0, 0, []);
        var all = series.SelectMany(it => it.Points).ToList();
        int firstTurn = all.Min(it => it.Turn), lastTurn = all.Max(it => it.Turn);
        int min = all.Min(it => it.Value), max = all.Max(it => it.Value);
        float X(int turn) => lastTurn == firstTurn ? 0.5f : (turn - firstTurn) / (float)(lastTurn - firstTurn);
        float Y(int value) => max == min ? 0.5f : 1f - (value - min) / (float)(max - min);
        var lines = series.Select(it => new GraphLine(Name(it.Civ), it.Civ.IsViewer, it.Civ.IsDefeated,
            it.Points.OrderBy(point => point.Turn).Select(point => new GraphPoint(X(point.Turn), Y(point.Value))).ToList())).ToList();
        return new GraphAxes(firstTurn, lastTurn, min, max, lines);
    }

    /// <summary>"3. Rome: 120", or "Rome (defeated): 40" without a place.</summary>
    public static string RowText(GameOverRankRow row)
    {
        string value = row.Value.ToString(CultureInfo.InvariantCulture);
        return row.IsDefeated ? $"{row.Civ} (defeated): {value}" : $"{row.Place.ToString(CultureInfo.InvariantCulture)}. {row.Civ}: {value}";
    }

    private static string Name(VictoryCivRef civ) => civ.CivName ?? UnknownCiv;
}
