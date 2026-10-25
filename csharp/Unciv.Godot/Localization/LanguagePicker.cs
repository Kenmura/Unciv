using Godot;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Settings;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Localization;

/// <summary>C025: the language choice. One drop-down of every language that has a translation file, with its completion percentage; choosing one saves it in the settings (which selects it).</summary>
public partial class LanguagePicker : OptionButton
{
    private readonly ClientLocalization localization;

    public LanguagePicker(ClientSettingsService settings, ClientLocalization localization)
    {
        this.localization = localization;
        Name = "LanguagePicker";
        TooltipText = "Language".Tr();
        foreach (var language in localization.Languages) AddItem(language.PercentComplete >= 100 ? language.Display : $"{language.Display} ({language.PercentComplete}%)");
        Select(IndexOf(settings.Current.Language));
        ItemSelected += index => settings.Update(s => s with { Language = localization.Languages[(int)index].Name });
    }

    private int IndexOf(string language)
    {
        for (int i = 0; i < localization.Languages.Count; i++)
        {
            if (localization.Languages[i].Name == language) return i;
        }
        return 0;
    }

    /// <summary>Picks a language as the player would (selecting it in the list); false for a language that has no file.</summary>
    public bool Choose(string language)
    {
        if (!localization.IsAvailable(language)) return false;
        Select(IndexOf(language));
        EmitSignal(OptionButton.SignalName.ItemSelected, IndexOf(language));
        return true;
    }
}
