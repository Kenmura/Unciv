namespace Unciv.Assets.Localization;

public static class ComposedText
{
    /// <summary>
    /// True for a line the view models composed from parts (it carries a digit or a <c>[]</c>, <c>{}</c>, <c>&lt;&gt;</c> marker, e.g. "Turn 5 - 4000 BC"). Such a line is not a translation key,
    /// and <c>Tr()</c> would re-format its numbers, so the client leaves it as it is until the models compose translatable sentences with placeholders.
    /// </summary>
    public static bool IsComposed(string text) => text.AsSpan().IndexOfAny("0123456789[]{}<>") >= 0;
}
