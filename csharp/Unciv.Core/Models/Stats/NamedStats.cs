namespace Unciv.Models.StatTypes;

public class NamedStats : Stats, INamed
{
    private string? name;

    /// <summary>Kotlin `lateinit var`: reading before assignment throws.</summary>
    public string Name
    {
        get => name ?? throw new InvalidOperationException("lateinit property name has not been initialized");
        set => name = value;
    }

    public override string ToString() => Name;

    public Stats CloneStats() => Clone();
}
