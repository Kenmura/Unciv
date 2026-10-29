namespace Unciv.Assets;

/// <summary>
/// The single table of placeholder colours (the only place colour values are written down). Known names get a hand-picked colour so
/// terrain reads correctly; every other name gets a colour derived from its stable hash, so mods render something distinguishable.
/// </summary>
public static class PlaceholderPalette
{
    private static readonly Dictionary<string, Rgb> TerrainColours = new(StringComparer.Ordinal)
    {
        ["Ocean"] = new Rgb(30, 70, 140),
        ["Coast"] = new Rgb(60, 120, 190),
        ["Lakes"] = new Rgb(70, 140, 200),
        ["Grassland"] = new Rgb(100, 170, 70),
        ["Plains"] = new Rgb(190, 180, 90),
        ["Desert"] = new Rgb(225, 200, 130),
        ["Tundra"] = new Rgb(150, 160, 140),
        ["Snow"] = new Rgb(240, 245, 250),
        ["Hill"] = new Rgb(130, 115, 70),
        ["Mountain"] = new Rgb(110, 105, 105),
        ["Forest"] = new Rgb(40, 110, 50),
        ["Jungle"] = new Rgb(20, 130, 70),
        ["Marsh"] = new Rgb(90, 130, 110),
        ["Oasis"] = new Rgb(60, 190, 160),
        ["Flood plains"] = new Rgb(150, 190, 90),
        ["Ice"] = new Rgb(200, 230, 245),
        ["Atoll"] = new Rgb(110, 200, 200),
        ["Fallout"] = new Rgb(120, 130, 60),
    };

    private static readonly Dictionary<string, Rgb> StatColours = new(StringComparer.Ordinal)
    {
        ["Production"] = new Rgb(160, 110, 60),
        ["Food"] = new Rgb(90, 170, 60),
        ["Gold"] = new Rgb(235, 190, 40),
        ["Science"] = new Rgb(70, 140, 230),
        ["Culture"] = new Rgb(170, 90, 200),
        ["Happiness"] = new Rgb(240, 150, 60),
        ["Faith"] = new Rgb(230, 230, 240),
    };

    private static readonly Dictionary<string, Rgb> CityTileColours = new(StringComparer.Ordinal)
    {
        ["Center"] = new Rgb(150, 120, 60),
        ["Worked"] = new Rgb(60, 130, 70),
        ["Taken"] = new Rgb(80, 90, 110),
        ["Available"] = new Rgb(70, 100, 150),
        ["Unavailable"] = new Rgb(60, 60, 66),
        ["Foreign"] = new Rgb(120, 60, 60),
        ["Unowned"] = new Rgb(40, 44, 52),
    };

    /// <summary>Fill of a city-screen tile cell by its state (C017); an unknown state reads as unowned.</summary>
    public static Rgb ForCityTile(string state) => CityTileColours.TryGetValue(state, out var c) ? c : CityTileColours["Unowned"];

    private static readonly Dictionary<string, Rgb> TechStateColours = new(StringComparer.Ordinal)
    {
        ["Researched"] = new Rgb(60, 130, 70),
        ["Queued"] = new Rgb(200, 150, 50),
        ["Researchable"] = new Rgb(70, 110, 170),
        ["Locked"] = new Rgb(48, 52, 62),
        ["Unavailable"] = new Rgb(40, 40, 44),
    };

    /// <summary>Fill of a tech tree node by its state (C019); an unknown state reads as locked.</summary>
    public static Rgb ForTechState(string state) => TechStateColours.TryGetValue(state, out var c) ? c : TechStateColours["Locked"];

    /// <summary>Text colour of a policy by its engine state (C020): adopted in the accent colour, adoptable in plain text, locked dimmed.</summary>
    public static Rgb ForPolicyState(string state) => state switch
    {
        "Adopted" => Accent,
        "Adoptable" => Text,
        _ => TextDisabled,
    };

    /// <summary>Outline of a locked tile on the city screen.</summary>
    private static readonly (string Name, Rgb Colour)[] LobbyColourTable =
    [
        ("Red", new Rgb(214, 70, 64)),
        ("Blue", new Rgb(70, 120, 220)),
        ("Green", new Rgb(80, 180, 90)),
        ("Yellow", new Rgb(230, 200, 70)),
        ("Purple", new Rgb(150, 90, 200)),
        ("Orange", new Rgb(235, 140, 50)),
        ("Teal", new Rgb(60, 180, 180)),
        ("Pink", new Rgb(230, 130, 180)),
    ];

    /// <summary>C035: the colour names a player can pick for their lobby seat, in display order.</summary>
    public static IReadOnlyList<string> LobbyColourNames { get; } = LobbyColourTable.Select(it => it.Name).ToList();

    /// <summary>C035: the swatch of a lobby colour name; a neutral grey for a name nobody here offers.</summary>
    public static Rgb ForLobbyColour(string? name) => LobbyColourTable.FirstOrDefault(it => it.Name == name) is { Name: not null } found ? found.Colour : TextDisabled;

    public static Rgb LockedOutline { get; } = new(240, 200, 90);

    public static Rgb HorseBrown { get; } = new(120, 80, 45);
    public static Rgb Background => UiTokens.Bg1;
    public static Rgb PanelFill => UiTokens.Surface1;
    public static Rgb PanelBorder => UiTokens.BorderSubtle;
    public static Rgb Accent => UiTokens.Accent;
    /// <summary>C012: overlay on the tiles the selected unit can move to.</summary>
    public static Rgb ReachableTint { get; } = new(70, 130, 220);
    /// <summary>C012: overlay on the tiles the selected unit can attack.</summary>
    public static Rgb AttackTint { get; } = new(220, 60, 50);
    public static Rgb AccentHover => UiTokens.AccentHi;
    public static Rgb ButtonFill => UiTokens.Surface2;
    public static Rgb ButtonHover => UiTokens.Surface3;
    public static Rgb ButtonPressed => UiTokens.Inset;
    public static Rgb Text => UiTokens.TextPrimary;
    public static Rgb ErrorText { get; } = new(255, 140, 128);
    public static Rgb TextDisabled => UiTokens.TextMuted;
    public static Rgb IconOutline { get; } = new(16, 18, 24);
    public static Rgb IconGlyph { get; } = new(250, 250, 250);
    public static Rgb RoadBrown { get; } = new(158, 115, 71);
    public static Rgb RailGrey { get; } = new(64, 64, 71);
    public static Rgb CityWall { get; } = new(158, 153, 143);
    /// <summary>C004: unit health bar fills and the bar's back plate.</summary>
    public static Rgb HealthGood { get; } = new(90, 200, 80);
    public static Rgb HealthWarn { get; } = new(235, 190, 50);
    public static Rgb HealthBad { get; } = new(220, 60, 50);
    public static Rgb HealthBack { get; } = new(20, 22, 28);
    /// <summary>C004: unit figure parts (head, metal, wood).</summary>
    public static Rgb UnitSkin { get; } = new(226, 190, 150);
    public static Rgb UnitMetal { get; } = new(170, 176, 188);
    public static Rgb UnitWood { get; } = new(122, 86, 52);
    /// <summary>C033: floating damage numbers, projectiles and the bombard flash.</summary>
    public static Rgb DamageNumber { get; } = new(255, 90, 70);
    public static Rgb Projectile { get; } = new(250, 230, 150);
    public static Rgb Flash { get; } = new(255, 200, 90);
    public static Rgb Light { get; } = new(255, 250, 240);
    public static Rgb SheetLabel { get; } = new(230, 230, 230);

    /// <summary>Map backdrop: what an unexplored tile looks like (nothing is drawn there).</summary>
    public static Rgb Unexplored { get; } = new(0, 0, 0);

    /// <summary>Multiplied into the albedo of an explored tile that is not currently visible.</summary>
    public static Rgb FogDim { get; } = new(120, 120, 140);

    private static readonly Dictionary<string, Rgb> StatusColours = new(StringComparer.Ordinal)
    {
        ["Fortified"] = new Rgb(90, 130, 200),
        ["Sleeping"] = new Rgb(140, 120, 200),
        ["Automated"] = new Rgb(80, 180, 120),
        ["PromotionAvailable"] = new Rgb(240, 200, 60),
    };

    /// <summary>C004: fill of a unit status mark; an unknown name gets a hash colour.</summary>
    public static Rgb ForStatus(string name) => StatusColours.TryGetValue(name, out var c) ? c : FromHash(name, 150, 210);

    public static Rgb ForTerrain(string name) => TerrainColours.TryGetValue(name, out var c) ? c : FromHash(name, 110, 170);

    public static Rgb ForStat(string name) => StatColours.TryGetValue(name, out var c) ? c : FromHash(name, 150, 210);

    /// <summary>Colour for any name: hue from its stable hash, fixed saturation and value so neighbours stay distinguishable.</summary>
    public static Rgb FromHash(string name, int saturation, int value) => Rgb.FromHsv((int)(StableHash.Of(name) % 360), saturation, value);
}
