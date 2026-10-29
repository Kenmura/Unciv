namespace Unciv.GodotClient.Ui;

/// <summary>
/// C062: the theme type variations of the UI (design/ui-redesign.md 2.5). A control sets <c>ThemeTypeVariation = UiVariants.ButtonPrimary</c>; no view builds a stylebox.
/// <c>ThemeBuilder</c> defines every entry of <see cref="All"/> with the base type given there (a test per variation checks it).
/// </summary>
public static class UiVariants
{
    // Buttons. The plain Button is the secondary style; ButtonSecondary names it explicitly.
    public const string ButtonPrimary = "ButtonPrimary";
    public const string ButtonSecondary = "ButtonSecondary";
    public const string ButtonGhost = "ButtonGhost";
    public const string ButtonDanger = "ButtonDanger";
    public const string ButtonIcon = "ButtonIcon";
    public const string ButtonTab = "ButtonTab";
    public const string ButtonChip = "ButtonChip";
    public const string ButtonSegment = "ButtonSegment";
    public const string ButtonRow = "ButtonRow";

    // City tiles (C069): a hex drawn from the variation's colours "hex_fill" and "hex_border"; one per engine tile state
    public const string CityTileCenter = "CityTileCenter";
    public const string CityTileWorked = "CityTileWorked";
    public const string CityTileTaken = "CityTileTaken";
    public const string CityTileAvailable = "CityTileAvailable";
    public const string CityTileUnavailable = "CityTileUnavailable";
    public const string CityTileForeign = "CityTileForeign";
    public const string CityTileUnowned = "CityTileUnowned";

    // Tech tree node cards (C070): one per research state, the pressed state is the selection
    public const string TechNodeResearched = "TechNodeResearched";
    public const string TechNodeCurrent = "TechNodeCurrent";
    public const string TechNodeQueued = "TechNodeQueued";
    public const string TechNodeResearchable = "TechNodeResearchable";
    public const string TechNodeLocked = "TechNodeLocked";

    // Panels
    public const string PanelCard = "PanelCard";
    public const string PanelInset = "PanelInset";
    public const string PanelModal = "PanelModal";
    public const string PanelTooltip = "PanelTooltip";
    public const string PanelHeader = "PanelHeader";
    public const string PanelDock = "PanelDock";
    public const string PanelSegmentTrack = "PanelSegmentTrack";
    public const string PanelBadge = "PanelBadge";
    public const string PanelBadgeAccent = "PanelBadgeAccent";
    public const string PanelKeyHint = "PanelKeyHint";
    public const string PanelCellEven = "PanelCellEven";
    public const string PanelCellOdd = "PanelCellOdd";

    // Labels: the type scale (Body is the default) and the two text tones
    public const string LabelDisplay = "LabelDisplay";
    public const string LabelTitle = "LabelTitle";
    public const string LabelHeading = "LabelHeading";
    public const string LabelBodyStrong = "LabelBodyStrong";
    public const string LabelNumber = "LabelNumber";
    public const string LabelSmall = "LabelSmall";
    public const string LabelOverline = "LabelOverline";
    public const string LabelSecondary = "LabelSecondary";
    public const string LabelMuted = "LabelMuted";

    // Other controls
    public const string ToggleSwitch = "ToggleSwitch";
    public const string StatBarThin = "StatBarThin";
    public const string StatBarLabelled = "StatBarLabelled";

    /// <summary>C069: the city tile variation of an engine tile state ("Center", "Worked", ...); an unknown state reads as unowned.</summary>
    public static string CityTile(string state) => state switch
    {
        "Center" => CityTileCenter,
        "Worked" => CityTileWorked,
        "Taken" => CityTileTaken,
        "Available" => CityTileAvailable,
        "Unavailable" => CityTileUnavailable,
        "Foreign" => CityTileForeign,
        _ => CityTileUnowned,
    };

    /// <summary>C070: the node card variation of a tech: the current research first, then by engine state ("Researched", "Queued", "Researchable", anything else reads as locked).</summary>
    public static string TechNode(string state, bool current) => current ? TechNodeCurrent : state switch
    {
        "Researched" => TechNodeResearched,
        "Queued" => TechNodeQueued,
        "Researchable" => TechNodeResearchable,
        _ => TechNodeLocked,
    };

    /// <summary>The city tile variations (they draw a hex instead of a box, so they have no focus ring stylebox).</summary>
    public static IReadOnlyList<string> CityTiles { get; } =
        [CityTileCenter, CityTileWorked, CityTileTaken, CityTileAvailable, CityTileUnavailable, CityTileForeign, CityTileUnowned];

    /// <summary>Every variation with the Godot control type it extends.</summary>
    public static IReadOnlyList<(string Name, string Base)> All { get; } =
    [
        (ButtonPrimary, "Button"), (ButtonSecondary, "Button"), (ButtonGhost, "Button"), (ButtonDanger, "Button"),
        (ButtonIcon, "Button"), (ButtonTab, "Button"), (ButtonChip, "Button"), (ButtonSegment, "Button"), (ButtonRow, "Button"),
        (CityTileCenter, "Button"), (CityTileWorked, "Button"), (CityTileTaken, "Button"), (CityTileAvailable, "Button"),
        (CityTileUnavailable, "Button"), (CityTileForeign, "Button"), (CityTileUnowned, "Button"),
        (TechNodeResearched, "Button"), (TechNodeCurrent, "Button"), (TechNodeQueued, "Button"), (TechNodeResearchable, "Button"), (TechNodeLocked, "Button"),
        (PanelCard, "PanelContainer"), (PanelInset, "PanelContainer"), (PanelModal, "PanelContainer"), (PanelTooltip, "PanelContainer"),
        (PanelHeader, "PanelContainer"), (PanelDock, "PanelContainer"), (PanelSegmentTrack, "PanelContainer"),
        (PanelBadge, "PanelContainer"), (PanelBadgeAccent, "PanelContainer"), (PanelKeyHint, "PanelContainer"), (PanelCellEven, "PanelContainer"), (PanelCellOdd, "PanelContainer"),
        (LabelDisplay, "Label"), (LabelTitle, "Label"), (LabelHeading, "Label"), (LabelBodyStrong, "Label"), (LabelNumber, "Label"),
        (LabelSmall, "Label"), (LabelOverline, "Label"), (LabelSecondary, "Label"), (LabelMuted, "Label"),
        (ToggleSwitch, "CheckButton"), (StatBarThin, "ProgressBar"), (StatBarLabelled, "ProgressBar"),
    ];
}
