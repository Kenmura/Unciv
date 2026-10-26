using Godot;
using Unciv.GodotClient.Localization;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Settings;

/// <summary>C043: the first settings page: the engine options of the human civ and the UI scale, each applied and saved the moment it changes. C025 adds the language picker, and the page's own text follows a language change.</summary>
public partial class SettingsPanel : VBoxContainer
{
    private readonly ClientSettingsService service;
    private readonly Label nameLabel = new();
    private readonly Label languageLabel = new();
    private readonly List<(Button Control, string Key)> texts = [];

    public SettingsPanel(ClientSettingsService service, Unciv.Assets.Localization.ClientLocalization? localization = null)
    {
        this.service = service;
        AddChild(nameLabel);
        if (localization != null)
        {
            var row = new HBoxContainer();
            row.AddChild(languageLabel);
            row.AddChild(new LanguagePicker(service, localization));
            AddChild(row);
        }
        AddToggle("Auto-assign city production", s => s.Player.AutoAssignCityProduction, (s, v) => s with { Player = s.Player with { AutoAssignCityProduction = v } });
        AddToggle("Automated units move on turn start", s => s.Player.AutomatedUnitsMoveOnTurnStart, (s, v) => s with { Player = s.Player with { AutomatedUnitsMoveOnTurnStart = v } });
        AddToggle("Cities auto bombard at end of turn", s => s.Player.CitiesAutoBombardAtEndOfTurn, (s, v) => s with { Player = s.Player with { CitiesAutoBombardAtEndOfTurn = v } });
        var scale = new Button();
        texts.Add((scale, "Bigger UI"));
        scale.Pressed += () => service.Update(s => s with { UiScale = OptionsModel.StepUiScale(s.UiScale, 1) });
        AddChild(scale);
        var smaller = new Button();
        texts.Add((smaller, "Smaller UI"));
        smaller.Pressed += () => service.Update(s => s with { UiScale = OptionsModel.StepUiScale(s.UiScale, -1) });
        AddChild(smaller);
        service.Changed += _ => Retranslate();
        Retranslate();
    }

    private void Retranslate()
    {
        var s = service.Current;
        nameLabel.Text = $"{s.PlayerName} - {"UI scale".Tr()} {s.UiScale:0.00}";
        languageLabel.Text = "Language".Tr();
        foreach (var (control, key) in texts) control.Text = key.Tr();
    }

    private void AddToggle(string key, Func<ClientSettings, bool> read, Func<ClientSettings, bool, ClientSettings> write)
    {
        var box = new CheckBox { ButtonPressed = read(service.Current) };
        texts.Add((box, key));
        box.Toggled += on => service.Update(s => write(s, on));
        AddChild(box);
    }
}
