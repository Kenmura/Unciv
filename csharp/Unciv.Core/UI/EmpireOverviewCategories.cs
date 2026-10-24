namespace Unciv.UI.Screens.OverviewScreen;

// core/src/com/unciv/ui/screens/overviewscreen/EmpireOverviewCategories.kt
// these belong to the client track. KeyCharAndCode(char) is reduced to the plain shortcut char.

/// <summary>This controls which Tabs for the EmpireOverviewScreen exist and their order.</summary>
public enum EmpireOverviewCategories
{
    Cities,
    Stats,
    Trades,
    Units,
    Politics,
    Resources,
    Religion,
    Wonders,
    Notifications,
}

public enum EmpireOverviewTabState
{
    Normal,
    Disabled,
    Hidden,
}

public static class EmpireOverviewCategoriesExtensions
{
    // libGDX Align bit flags
    public const int AlignTop = 2;
    public const int AlignLeft = 8;
    public const int AlignTopLeft = AlignTop | AlignLeft;

    public static string IconName(this EmpireOverviewCategories category) => category switch
    {
        EmpireOverviewCategories.Cities => "OtherIcons/Cities",
        EmpireOverviewCategories.Stats => "StatIcons/Gold",
        EmpireOverviewCategories.Trades => "StatIcons/Acquire",
        EmpireOverviewCategories.Units => "OtherIcons/Shield",
        EmpireOverviewCategories.Politics => "OtherIcons/Politics",
        EmpireOverviewCategories.Resources => "StatIcons/Happiness",
        EmpireOverviewCategories.Religion => "StatIcons/Faith",
        EmpireOverviewCategories.Wonders => "OtherIcons/Wonders",
        EmpireOverviewCategories.Notifications => "OtherIcons/Notifications",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static char ShortcutChar(this EmpireOverviewCategories category) => category switch
    {
        EmpireOverviewCategories.Cities => 'C',
        EmpireOverviewCategories.Stats => 'S',
        EmpireOverviewCategories.Trades => 'T',
        EmpireOverviewCategories.Units => 'U',
        EmpireOverviewCategories.Politics => 'P',
        EmpireOverviewCategories.Resources => 'R',
        EmpireOverviewCategories.Religion => 'F',
        EmpireOverviewCategories.Wonders => 'W',
        EmpireOverviewCategories.Notifications => 'N',
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static int ScrollAlign(this EmpireOverviewCategories category) => category switch
    {
        EmpireOverviewCategories.Cities => AlignTopLeft,
        EmpireOverviewCategories.Units => AlignTopLeft,
        EmpireOverviewCategories.Resources => AlignTopLeft,
        _ => AlignTop,
    };
}
