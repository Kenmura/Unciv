using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset.Tech;
using Unciv.UI.ObjectDescriptions;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Queries.Empire;

// Name: Technology name
// Era: Era name of its column
// Column: Column number in the tree (null when the ruleset gives the tech none)
// Row: Row in the tree
// Cost: Science needed now (CostOfTech)
// Progress: Science already invested
// Prerequisites: Names of the techs that must be researched first
// State: Researched, Queued (in the research queue), Researchable, Locked (a prerequisite is missing) or Unavailable (the ruleset hides it for this civ)
// QueuePosition: 0 for the current research, null when not queued
// Turns: Turns until it is done at the current science rate (0 when nothing remains), null when science per turn is not positive
// Description: What the tech unlocks, the text of the tech picker
public sealed record TechTreeEntry(string Name, string Era, int? Column, int Row, int Cost, int Progress,
    IReadOnlyList<string> Prerequisites, string State, int? QueuePosition, int? Turns, string Description);

public sealed record TechTreeResult(string? CurrentTech, IReadOnlyList<string> Queue, int FreeTechs, bool ShouldOpenPicker,
    IReadOnlyList<TechTreeEntry> Techs);

// T197: the tech tree and research queue of the viewer. Wraps TechManager (CostOfTech, TurnsToTech arithmetic, CanBeResearched) and
// TechnologyDescriptions, the text the tech picker shows. Nothing here mutates: it reads TechsToResearch, never reorders it.
[QueryType("empire.TechTree")]
public sealed record TechTreeQuery : IQuery<TechTreeResult>
{
    public string Type => "empire.TechTree";

    public TechTreeResult Run(GameView view)
    {
        var civView = view.CivView;
        var civ = civView.Unwrap();
        var tech = civ.Tech;
        var queue = tech.TechsToResearch.ToList();
        var entries = new List<TechTreeEntry>();
        foreach (var technology in civ.GameInfo.Ruleset.Technologies.Values.OrderBy(it => it.Name, StringComparer.Ordinal))
        {
            string state = StateOf(civ, technology, queue);
            int position = queue.IndexOf(technology.Name);
            entries.Add(new TechTreeEntry(technology.Name, technology.Column == null ? "" : technology.Era(), technology.Column?.ColumnNumber,
                technology.Row, tech.CostOfTech(technology.Name), tech.ResearchOfTech(technology.Name),
                technology.Prerequisites.ToList(), state, position < 0 ? null : position,
                state == "Researched" ? null : Turns(civ, technology.Name),
                TechnologyDescriptions.GetDescription(technology, civ)));
        }
        return new TechTreeResult(tech.CurrentTechnologyName(), queue, tech.FreeTechs, civView.ShouldOpenTechPicker(), entries);
    }

    private static string StateOf(Civilization civ, Technology technology, List<string> queue)
    {
        var tech = civ.Tech;
        if (tech.IsResearched(technology.Name) && !technology.IsContinuallyResearchable()) return "Researched";
        if (tech.IsUnresearchable(technology)) return "Unavailable";
        if (queue.Contains(technology.Name)) return "Queued";
        return tech.CanBeResearched(technology.Name) ? "Researchable" : "Locked";
    }

    // Same arithmetic as TechManager.TurnsToTech, which returns translated text.
    private static int? Turns(Civilization civ, string techName)
    {
        double remaining = civ.Tech.RemainingScienceToTech(techName);
        if (remaining <= 0) return 0;
        float science = civ.Stats.StatsForNextTurn.Science;
        if (science <= 0f) return null;
        return Math.Max(1, KotlinMath.DoubleToInt(Math.Ceiling(remaining / science)));
    }
}
