namespace Unciv.Core.Tests.Support;

// Port of tests/src/com/unciv/testing/TestParcours.kt (inline form): run the testee on every input, collect all failures and fail once.
internal static class TestParcours
{
    public static void Run<T, R>(string title, Func<T, R> inputToActual, params (T Input, R Expected)[] cases)
    {
        var failures = cases
            .Select(it => (it.Input, it.Expected, Actual: inputToActual(it.Input)))
            .Where(it => !EqualityComparer<R>.Default.Equals(it.Actual, it.Expected))
            .Select(it => $"Input: \"{it.Input}\", Expected: {it.Expected}, Actual: {it.Actual}")
            .ToList();
        Assert.True(failures.Count == 0, $"{title} failures:\n{string.Join("\n", failures)}");
    }
}
