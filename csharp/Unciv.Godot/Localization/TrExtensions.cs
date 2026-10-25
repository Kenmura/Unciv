using Unciv.Assets.Localization;
using Unciv.Models.Translations;

namespace Unciv.GodotClient.Localization;

public static class TrExtensions
{
    /// <summary>
    /// Translates text the screens get from the view models. A fixed phrase ("Next turn") is a translation key; a line that carries a number or a
    /// <c>[]</c>, <c>{}</c> or <c>&lt;&gt;</c> marker is already composed output ("Turn 5 - 4000 BC") and is left alone, because <c>Tr()</c> would re-format its numbers.
    /// Composing translatable sentences with placeholders in the models is the later step (C027 and the model tickets).
    /// </summary>
    public static string TrText(this string text) =>
        ComposedText.IsComposed(text) ? text : text.Tr();
}
