using Unciv.Models.Ruleset.Nation;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.UI.ObjectDescriptions;

// core/src/com/unciv/ui/objectdescriptions/NationDescriptions.kt, only Personality.getCivilopediaTextHeaderImpl and Personality.getPersonalityCivilopediaTextLines
// (the rest of the file belongs to the Nation descriptions).

public static class PersonalityDescriptions
{
    public static FormattedLine GetCivilopediaTextHeaderImpl(this Personality personality) =>
        new($"[{personality.Name}]'s personality", icon: personality.MakeLink(), header: 2);

    public static List<FormattedLine> GetPersonalityCivilopediaTextLines(this Personality personality, CivilopediaContext context) =>
        FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            if (personality.IsNeutralPersonality)
            {
                b.Add(new FormattedLine("Neutral personality"));
                return;
            }

            var nations = context.Ruleset.Nations.Values.Where(it => it.Personality == personality.Name).ToList();
            if (nations.Count != 0)
            {
                foreach (var nation in nations)
                    b.Add(new FormattedLine($"See also: [{nation.Name}]", link: nation.MakeLink()));
                b.Add(new FormattedLine());
            }

            if (personality.PreferredVictoryType.Length != 0 && personality.PreferredVictoryType != Constants.NeutralVictoryType)
            {
                b.Add(new FormattedLine($"Preferred victory type: [{personality.PreferredVictoryType}]"));
                b.Add(new FormattedLine());
            }

            static int ToPercent(float value) => KotlinNumberText.FloatToInt((value - 5f) * 20f);
            var biases = new List<FormattedLine>();
            foreach (var focus in Enum.GetValues<PersonalityValue>())
            {
                var value = personality[focus];
                if (value == 5f) continue;
                biases.Add(new FormattedLine($"{{{focus.GetDescription()}}}: {KotlinNumberText.Int(ToPercent(value))}%", indent: 1));
            }
            if (biases.Count != 0)
            {
                b.Add("Biases:");
                b.Add(biases);
                b.Space();
            }

            if (personality.Priorities.Count == 0) return;
            b.Add("Policy priorities:");
            foreach (var (name, value) in personality.Priorities)
                b.Add($"[{name}]: [{KotlinNumberText.Int(value)}]", indent: 1);
        });
}
