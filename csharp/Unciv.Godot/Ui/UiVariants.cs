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

    // Panels
    public const string PanelCard = "PanelCard";
    public const string PanelInset = "PanelInset";
    public const string PanelModal = "PanelModal";
    public const string PanelTooltip = "PanelTooltip";
    public const string PanelHeader = "PanelHeader";
    public const string PanelDock = "PanelDock";
    public const string PanelSegmentTrack = "PanelSegmentTrack";

    // Labels: the type scale (Body is the default) and the two text tones
    public const string LabelDisplay = "LabelDisplay";
    public const string LabelTitle = "LabelTitle";
    public const string LabelHeading = "LabelHeading";
    public const string LabelBodyStrong = "LabelBodyStrong";
    public const string LabelSmall = "LabelSmall";
    public const string LabelOverline = "LabelOverline";
    public const string LabelSecondary = "LabelSecondary";
    public const string LabelMuted = "LabelMuted";

    // Other controls
    public const string ToggleSwitch = "ToggleSwitch";
    public const string StatBarThin = "StatBarThin";
    public const string StatBarLabelled = "StatBarLabelled";

    /// <summary>Every variation with the Godot control type it extends.</summary>
    public static IReadOnlyList<(string Name, string Base)> All { get; } =
    [
        (ButtonPrimary, "Button"), (ButtonSecondary, "Button"), (ButtonGhost, "Button"), (ButtonDanger, "Button"),
        (ButtonIcon, "Button"), (ButtonTab, "Button"), (ButtonChip, "Button"), (ButtonSegment, "Button"),
        (PanelCard, "PanelContainer"), (PanelInset, "PanelContainer"), (PanelModal, "PanelContainer"), (PanelTooltip, "PanelContainer"),
        (PanelHeader, "PanelContainer"), (PanelDock, "PanelContainer"), (PanelSegmentTrack, "PanelContainer"),
        (LabelDisplay, "Label"), (LabelTitle, "Label"), (LabelHeading, "Label"), (LabelBodyStrong, "Label"),
        (LabelSmall, "Label"), (LabelOverline, "Label"), (LabelSecondary, "Label"), (LabelMuted, "Label"),
        (ToggleSwitch, "CheckButton"), (StatBarThin, "ProgressBar"), (StatBarLabelled, "ProgressBar"),
    ];
}
