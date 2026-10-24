using System.Globalization;
using Unciv.Logic.Civilization;
using Unciv.Utils;

namespace Unciv.UI.Components;

// core/src/com/unciv/ui/components/MayaCalendar.kt
// Stripped: openPopup (Popup/BaseScreen). startTurnForMaya (T070): NextTurnAutomation.chooseGreatPerson is the INextTurnAutomation hook
// on the ClientState, skipped when none is set.
public static class MayaCalendar
{
    private const string IconFolder = "MayaCalendar/";
    public const string NotificationIcon = "MayaCalendar/Maya";
    public const string TunIcon = "MayaCalendar/Tun";
    public const string KatunIcon = "MayaCalendar/Katun";
    public const string BaktunIcon = "MayaCalendar/Baktun";
    public const char Tun = 'ම';
    public const char Katun = 'ඹ';
    public const char Baktun = 'ය';

    // The mayan numerals are actually unicode U+1D2E0 to U+1D2F3, but we can't do those
    // so the code points for small roman numerals U+2170 to U+2183 are used instead
    public const char Zero = 'ⅰ';
    public const char Nineteen = 'Ↄ';

    public static IReadOnlyList<char> Digits { get; } = Enumerable.Range(Zero, Nineteen - Zero + 1).Select(it => (char)it).ToArray();

    public static string DigitIcon(char ch) => IconFolder + (ch - Zero).ToString(CultureInfo.InvariantCulture);

    public static IReadOnlyList<(char Symbol, string Icon)> AllSymbols { get; } =
        new[] { (Tun, TunIcon), (Katun, KatunIcon), (Baktun, BaktunIcon) }
            .Concat(Digits.Select(it => (it, DigitIcon(it))))
            .Take(23)
            .ToArray();

    public static string ToMayaNumerals(this int number)
    {
        var digits = new LinkedList<int>();
        int residue = number;
        while (true)
        {
            digits.AddLast(residue % 20);
            residue /= 20;
            if (residue == 0) break;
        }

        var sb = new System.Text.StringBuilder();
        while (digits.Count > 0)
        {
            int last = digits.Last.NotNull().Value;
            digits.RemoveLast();
            sb.Append(unchecked((char)(Zero + last)));
        }

        return sb.ToString();
    }

    private const int DaysOn30000101Bce = 36000 + 5040 + 240 + 11;

    private sealed class MayaYear
    {
        public int Baktuns { get; }

        public int Katuns { get; }

        public int Tuns { get; }

        public MayaYear(int year)
        {
            int mayaDays = ((year + 3000) * 365) + ((year + 3000) / 4) + DaysOn30000101Bce;
            int totalTuns = mayaDays >= 0 ? mayaDays / 360 : (13 * 20 * 20) + (mayaDays / 360);
            int totalKatuns = totalTuns / 20;
            Baktuns = totalKatuns / 20;
            Katuns = totalKatuns - (Baktuns * 20);
            Tuns = totalTuns - (totalKatuns * 20);
        }

        public override string ToString()
        {
            char baktunDigit = unchecked((char)(Zero + Baktuns));
            char katunDigit = unchecked((char)(Zero + Katuns));
            char tunDigit = unchecked((char)(Zero + Tuns));
            return $"{baktunDigit}{Baktun}{katunDigit}{Katun}{tunDigit}{Tun}";
        }
    }

    public static string YearToMayaDate(int year) => new MayaYear(year).ToString();

    /// <summary>The b'ak'tun of a year.</summary>
    public static int YearToBaktuns(int year) => new MayaYear(year).Baktuns;

    private static bool IsNewCycle(int year, int otherYear) => new MayaYear(year).Baktuns != new MayaYear(otherYear).Baktuns;

    public static void StartTurnForMaya(Civilization civInfo)
    {
        var game = civInfo.GameInfo;
        int year = game.GetYear(0);
        if (!IsNewCycle(year, game.GetYear(-1))) return;
        civInfo.GreatPeople.TriggerMayanGreatPerson();
        if (civInfo.IsAI() || game.IsFullAutoPlayAI)
            game.ClientState.NextTurnAutomation?.ChooseGreatPerson(civInfo);
    }
}
