using Unciv.Utils;

namespace Unciv;

// core/src/com/unciv/Constants.kt (members keep their Kotlin names in PascalCase)
public static class Constants
{
    public const string Settler = "Settler";
    public const string EraSpecificUnit = "Era Starting Unit";
    public const string LowercaseAll = "all";
    public const string UppercaseAll = "All";
    public static readonly IReadOnlySet<string> All = new LinkedHashSet<string>([UppercaseAll, LowercaseAll]);
    public const int NoId = -1;

    public const string English = "English";

    // Terrain
    public const string Impassable = "Impassable";
    public const string Ocean = "Ocean";

    // The "Coast" _terrain_
    // @see com.unciv.models.ruleset.tile.Terrain.isCoast
    public const string Coast = "Coast";
    // The "Coastal" terrain _filter_
    public const string Coastal = "Coastal";

    // Used as filter and the name of the pseudo-TerrainFeature defining river Stats
    public const string River = "River";

    public const string Mountain = "Mountain";
    public const string Hill = "Hill";
    public const string Plains = "Plains";
    public const string Desert = "Desert";
    public const string Grassland = "Grassland";
    // Upstream note: GameStarter places "Tundra" startBias first. Can we make it generic by checking count by terrain?
    public const string Tundra = "Tundra";
    public const string Snow = "Snow";

    public const string Forest = "Forest";
    public const string Jungle = "Jungle";
    public const string Ice = "Ice";
    public static readonly IReadOnlyList<string> Vegetation = [Forest, Jungle];

    // Note the difference in case. **Not** interchangeable!
    // Upstream note: this is very opaque behaviour to modders
    // The "Fresh water" terrain _unique_
    public const string FreshWater = "Fresh water";
    // The "Fresh Water" terrain _filter_
    public const string FreshWaterFilter = "Fresh Water";

    public const string BarbarianEncampment = "Barbarian encampment";
    public const string CityCenter = "City center";
    public const string AllRoad = "All Road";

    // Treaties
    public const string PeaceTreaty = "Peace Treaty";
    public const string ResearchAgreement = "Research Agreement";
    public const string DefensivePact = "Defensive Pact";

    // Agreements
    public const string OpenBorders = "Open Borders";

    // Other trade items
    public const string AcceptEmbassy = "Accept Embassy";
    public const string GoldPerTurn = "Gold per turn";
    public const string FlatGold = "Gold";

    // Used as origin in StatMap or ResourceSupplyList, or the toggle button in DiplomacyOverviewTab
    public const string CityStates = "City-States";
    // Used as origin in ResourceSupplyList
    public const string Tradable = "Tradable";

    public const string Random = "Random";
    public const string UnknownNationName = "???";
    public const string UnknownCityName = "???";

    public const string Fort = "Fort";

    public const string FutureTech = "Future Tech";
    // Easter egg name. Is to avoid conflicts when players name their own religions.
    // This religion name should never be displayed.
    public const string NoReligionName = "The religion of TheLegend27";
    public const string SpyHideout = "Spy Hideout";

    public const string NeutralVictoryType = "Neutral";

    public const string CancelImprovementOrder = "Cancel improvement order";
    public const string TutorialPopupNamePrefix = "Tutorial: ";
    public const string ThisUnit = "This Unit";
    public const string TargetUnit = "Target Unit";

    public const string OK = "OK";
    public const string Close = "Close";
    public const string Cancel = "Cancel";
    public const string Yes = "Yes";
    public const string No = "No";
    public const string Loading = "Loading...";
    public const string Working = "Working...";

    public const string Barbarians = "Barbarians";
    public const string Spectator = "Spectator";
    public const string HumanPlayer = "Human player";
    public const string AiPlayer = "AI player";

    public const string Embarked = "Embarked";
    public const string Wounded = "Wounded";

    public const string Remove = "Remove ";
    public const string Repair = "Repair";
    // [com.unciv.logic.map.mapunit.MapUnit.action] value while automatically connecting a road
    public const string ConnectRoadAction = "Connect road";

    public const string UniqueOrDelimiter = "\" OR \"";
    public const char StringSplitCharacter = '␟'; // U+241 - Unit separator character. Used to join texts and split them with a char that is virtually guaranteed to not be used in normal text.


    public const string SimulationCiv1 = "SimulationCiv1";
    public const string SimulationCiv2 = "SimulationCiv2";

    public const string DropboxMultiplayerServer = "Dropbox";
    public const string UncivXyzServer = "https://uncivserver.xyz";

    public const string DefaultTileset = "HexaRealm";
    // Default for TileSetConfig.fallbackTileSet - Don't change unless you've also moved the crosshatch, borders, and arrows as well
    public const string DefaultFallbackTileset = "FantasyHex";
    public const string DefaultUnitset = "AbsoluteUnits";
    public const string DefaultSkin = "Minimal";
    public const string DefaultFallbackSkin = "Minimal";

    // Use this to determine whether a [MapUnit][com.unciv.logic.map.mapunit.MapUnit]'s movement is exhausted
    // (currentMovement <= this) if and only if a fuzzy comparison is needed to account for Float rounding errors.
    // _Most_ checks do compare to 0!
    public const float MinimumMovementEpsilon = 0.05f; // 0.1f was used previously, too - here for global searches
    public const float AiPreferInquisitorOverMissionaryPressureDifference = 3000f;

    public const int DefaultFontSize = 18;
    public const int HeadingFontSize = 24;
    public const int SmallerHeadingFontSize = 20;

    // URL to the root of the Unciv repository, including trailing slash
    // Note: Should the project move, this covers external links, but not comments e.g. mentioning issues
    public const string UncivRepoURL = "https://github.com/yairm210/Unciv/";
    // URL to the wiki, including trailing slash
    public const string WikiURL = "https://yairm210.github.io/Unciv/";

    // Gdx's own default (only visible in the ActorGestureListener() constructor) for the "tap square" -
    // distance the pointer can travel between tap events while still being considered the same tap.
    // Gdx docs say unit is pixels, but they're actually stage units for an ActorGestureListener.
    public const float DefaultHalfTapSquareSize = 20f;

}
