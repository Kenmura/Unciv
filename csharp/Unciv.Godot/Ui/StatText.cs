using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// C065: a line of text whose Unciv stat glyphs (food, gold, ...) are drawn as the stat icons, inline at the text height, and whose signed numbers can be
/// coloured by sign. A plain Label cannot do either, so views that show "3 ¤ +2" use this.
/// </summary>
public partial class StatText : RichTextLabel
{
    private readonly UiTheme theme;

    public StatText(UiTheme theme, string text = "")
    {
        this.theme = theme;
        BbcodeEnabled = true;
        FitContent = true;
        ScrollActive = false;
        AutowrapMode = TextServer.AutowrapMode.Off;
        MouseFilter = MouseFilterEnum.Ignore;
        Set(text);
    }

    /// <summary>The stats whose icons the current text shows, in order.</summary>
    public IReadOnlyList<string> Stats { get; private set; } = [];

    /// <summary>Replaces the text: each stat glyph becomes its icon; other characters stay as they are (no markup is interpreted in <paramref name="text"/>).</summary>
    public void Set(string text)
    {
        Clear();
        var stats = new List<string>();
        int size = theme.Px(UiTheme.IconSize) * 2 / 3;
        foreach (var run in UiText.Runs(text))
        {
            if (run.Stat is { } stat)
            {
                AddImage(theme.Icon(AssetKey.Of(RulesetAssetKeys.StatIcon, stat)), size, size);
                stats.Add(stat);
            }
            else
            {
                AddText(run.Text);
            }
        }
        Stats = stats;
    }

    /// <summary>Appends a signed number coloured by its sign (<see cref="UiText.Delta"/>).</summary>
    public void AppendDelta(int value) => AppendText(UiText.Delta(value));
}
