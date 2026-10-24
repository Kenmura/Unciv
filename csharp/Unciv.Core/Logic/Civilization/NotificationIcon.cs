namespace Unciv.Logic.Civilization;

// core/src/com/unciv/logic/civilization/NotificationIcons.kt (object NotificationIcon)
// For use in the notificationIcons parameter of Civilization.addNotification: texture paths or some ruleset object names.
// Stripped: getImage(icon, ruleset, iconSize), which builds libGDX images (client track).
public static class NotificationIcon
{
    // Remember: The typical white-on-transparency icon will not be visible on Notifications

    public const string Barbarians = "ImprovementIcons/Barbarian encampment";
    public const string City = "ImprovementIcons/City center";
    public const string CityState = "OtherIcons/CityState";
    public const string Crosshair = "OtherIcons/CrosshairB";
    public const string Culture = "StatIcons/Culture";
    public const string Construction = "StatIcons/Production";
    public const string Death = "OtherIcons/DisbandUnit";
    public const string Diplomacy = "OtherIcons/Diplomacy";
    public const string Faith = "StatIcons/Faith";
    public const string Food = "StatIcons/Food";
    public const string Gold = "StatIcons/Gold";
    public const string Growth = "StatIcons/Population";
    public const string Happiness = "StatIcons/Happiness";
    public const string Population = "StatIcons/Population";
    public const string Production = "StatIcons/Production";
    public const string Question = "OtherIcons/Question";
    public const string Ruins = "ImprovementIcons/Ancient ruins";
    public const string Science = "StatIcons/Science";
    public const string Scout = "UnitIcons/Scout";
    public const string Spy = "OtherIcons/Spy";
    public const string Trade = "StatIcons/Acquire";
    public const string War = "OtherIcons/Pillage";
    public const string Quest = "OtherIcons/Quest";
}
