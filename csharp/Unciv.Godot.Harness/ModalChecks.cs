using Godot;
using Unciv.GodotClient.Ui;

namespace Unciv.GodotHarness;

/// <summary>C066: the checks every popup of the client must pass, so the scenarios assert "one frame" the same way.</summary>
internal static class ModalChecks
{
    /// <summary>
    /// The modal is on screen inside the window, centred, between <see cref="Modal.MinWidth"/> and <see cref="Modal.MaxWidth"/> (never more than 90% of the window), carries the accent
    /// hairline along its top edge, has a title, and its footer holds at least one button, all inside the frame, with the primary one right-most when they sit in a row.
    /// </summary>
    public static bool Framed(Modal modal)
    {
        var theme = modal.Ui;
        if (!modal.IsVisibleInTree()) return false;
        var window = new Rect2(Vector2.Zero, modal.GetViewport().GetVisibleRect().Size);
        var frame = modal.Frame.GetGlobalRect();
        float max = Math.Min(theme.Px(Modal.MaxWidth), window.Size.X * Modal.ViewportShare) + 1f;
        float min = Math.Min(theme.Px(Modal.MinWidth), window.Size.X * Modal.ViewportShare) - 1f;
        bool size = window.Encloses(frame) && frame.Size.X >= min && frame.Size.X <= max;
        bool centred = Math.Abs(frame.GetCenter().X - window.GetCenter().X) <= 1f;
        var line = modal.HairlineRect;
        bool hairline = line.Size.X > 0 && line.Size.Y >= 1 && Math.Abs(line.Position.Y - frame.Position.Y) <= 0.5f && frame.Encloses(line) && modal.Frame.LineColour == Unciv.GodotClient.Assets.ProceduralAssets.ToColor(Unciv.Assets.UiTokens.Accent);
        var actions = modal.Actions;
        bool footer = actions.Count > 0 && actions.All(it => it.Visible && frame.Encloses(it.GetGlobalRect()));
        bool primaryLast = modal.Stacked || actions.Any(it => it.ThemeTypeVariation == UiVariants.ButtonPrimary) == (actions[^1].ThemeTypeVariation == UiVariants.ButtonPrimary);
        return size && centred && hairline && footer && primaryLast && modal.TitleLabel.Text.Length > 0 && modal.TitleLabel.ThemeTypeVariation == UiVariants.LabelHeading;
    }
}
