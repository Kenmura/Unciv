using Unciv.Models.Translations;

namespace Unciv.Logic.Simulation;

// Ported from core/src/com/unciv/logic/simulation/MutableInt.kt (T101).
// Kotlin `++value` / `value += addend` wrap on overflow; C# arithmetic is unchecked by default as well.

/// <summary>A boxed counter that the simulation statistics maps share by reference.</summary>
public sealed class MutableInt(int value = 0)
{
    public int Value { get; set; } = value;

    public void Inc() => Value++;

    public int Get() => Value;

    public void Set(int newValue) => Value = newValue;

    public void Add(int addend) => Value += addend;

    public override string ToString() => Value.Tr();
}
