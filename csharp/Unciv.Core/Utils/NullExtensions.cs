using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Unciv.Utils;

/// <summary>Kotlin's <c>!!</c>: throws immediately instead of letting a null travel on (C# <c>!</c> only silences the compiler).</summary>
public static class NullExtensions
{
    /// <summary>Kotlin <c>value!!</c> for reference types. Throws <see cref="NullReferenceException"/> (Kotlin: NullPointerException).</summary>
    public static T NotNull<T>([NotNull] this T? value, [CallerArgumentExpression(nameof(value))] string? expression = null) where T : class =>
        value ?? throw new NullReferenceException(expression == null ? "Value is null" : $"'{expression}' is null");

    /// <summary>Kotlin <c>value!!</c> for nullable value types.</summary>
    public static T NotNull<T>([NotNull] this T? value, [CallerArgumentExpression(nameof(value))] string? expression = null) where T : struct =>
        value ?? throw new NullReferenceException(expression == null ? "Value is null" : $"'{expression}' is null");
}
