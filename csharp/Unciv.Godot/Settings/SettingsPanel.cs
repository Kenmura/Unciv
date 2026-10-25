using Godot;

namespace Unciv.GodotClient.Settings;

/// <summary>C043: the first settings page: the engine options of the human civ and the UI scale, each applied and saved the moment it changes.</summary>
public partial class SettingsPanel : VBoxContainer
{
    private readonly ClientSettingsService service;

    public SettingsPanel(ClientSettingsService service)
    {
        this.service = service;
        var nameLabel = new Label();
        AddChild(nameLabel);
        AddToggle("Auto-assign city production", s => s.Player.AutoAssignCityProduction, (s, v) => s with { Player = s.Player with { AutoAssignCityProduction = v } });
        AddToggle("Automated units move on turn start", s => s.Player.AutomatedUnitsMoveOnTurnStart, (s, v) => s with { Player = s.Player with { AutomatedUnitsMoveOnTurnStart = v } });
        AddToggle("Cities auto bombard at end of turn", s => s.Player.CitiesAutoBombardAtEndOfTurn, (s, v) => s with { Player = s.Player with { CitiesAutoBombardAtEndOfTurn = v } });
        var scale = new Button { Text = "Bigger UI" };
        scale.Pressed += () => service.Update(s => s with { UiScale = Math.Min(4f, s.UiScale + 0.25f) });
        AddChild(scale);
        service.Changed += s => nameLabel.Text = $"{s.PlayerName} - UI scale {s.UiScale:0.00}";
        nameLabel.Text = $"{service.Current.PlayerName} - UI scale {service.Current.UiScale:0.00}";
    }

    private void AddToggle(string text, Func<ClientSettings, bool> read, Func<ClientSettings, bool, ClientSettings> write)
    {
        var box = new CheckBox { Text = text, ButtonPressed = read(service.Current) };
        box.Toggled += on => service.Update(s => write(s, on));
        AddChild(box);
    }
}
