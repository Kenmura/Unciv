using Godot;

namespace Unciv.GodotClient.Settings;

/// <summary>C028: puts the machine-level settings into effect on the running window: master volume, window mode and UI scale. Called at start and after every change.</summary>
public static class ClientSettingsApplier
{
    public static void Apply(ClientSettings settings, Window? window)
    {
        AudioServer.SetBusVolumeDb(0, OptionsModel.VolumeDb(settings.SoundVolume));
        if (window == null) return;
        window.MinSize = new Vector2I(Unciv.Assets.UiTokens.MinWindowWidth, Unciv.Assets.UiTokens.MinWindowHeight); // T311
        window.Mode = settings.WindowMode switch
        {
            "Fullscreen" => Window.ModeEnum.ExclusiveFullscreen,
            "Borderless" => Window.ModeEnum.Fullscreen,
            _ => Window.ModeEnum.Windowed,
        };
        window.ContentScaleMode = settings.UiScale == 1f ? Window.ContentScaleModeEnum.Disabled : Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleFactor = settings.UiScale;
    }
}
