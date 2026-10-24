using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/UnitType.kt
// Not ported yet, they need the Civilopedia text types (T034): makeLink, getCivilopediaTextLines, getSortGroup, getSubCategory.

/// <summary>The types of tiles the unit can by default enter</summary>
public enum UnitMovementType
{
    /// <summary>Only land tiles except when certain techs are researched</summary>
    Land,
    /// <summary>Only water tiles</summary>
    Water,
    /// <summary>Only city tiles and carrying units</summary>
    Air,
}

public class UnitType : RulesetObject
{
    public override string MakeLink() => $"UnitType/{Name}";

    private readonly Lazy<UnitMovementType?> unitMovementType;

    public UnitType()
    {
        unitMovementType = new Lazy<UnitMovementType?>(() => MovementType == null ? null : ParseMovementType(MovementType));
    }

    public UnitType(string name, string? domain = null) : this()
    {
        Name = name;
        MovementType = domain;
    }

    public string? MovementType { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.UnitType;

    /// <summary>Kotlin <c>UnitMovementType.valueOf</c>: exact name or an exception</summary>
    private static UnitMovementType ParseMovementType(string name) =>
        Enum.GetNames<UnitMovementType>().Contains(name)
            ? Enum.Parse<UnitMovementType>(name)
            : throw new ArgumentException($"No enum constant UnitMovementType.{name}");

    public UnitMovementType? GetMovementType() => unitMovementType.Value;

    public bool IsLandUnit() => unitMovementType.Value == UnitMovementType.Land;
    public bool IsWaterUnit() => unitMovementType.Value == UnitMovementType.Water;
    public bool IsAirUnit() => unitMovementType.Value == UnitMovementType.Air;

    /// <summary>Implements UniqueParameterType.UnitTypeFilter</summary>
    public bool MatchesFilter(string filter) => filter switch
    {
        "Land" => IsLandUnit(),
        "Water" => IsWaterUnit(),
        "Air" => IsAirUnit(),
        _ => filter == Name || ((IHasUniques)this).HasTagUnique(filter, null),
    };

    public bool IsUsed(IRulesetConstructionView ruleset) => ruleset.GetAllUnits().Any(it => it.UnitType == Name);

    public static readonly UnitType City = new("City", "Land");

    public static IReadOnlyCollection<UnitType> GetCivilopediaIterator(IRulesetConstructionView ruleset)
    {
        // Create virtual UnitTypes to describe the movement domains - Civilopedia only.
        // It is important that the name includes the [] _everywhere_
        // (here, CivilopediaImageGetters, links, etc.) so translation comes as cheap as possible.
        return Enum.GetValues<UnitMovementType>().Select(it => new UnitType($"Domain: [{it}]", it.ToString()))
            .Concat(ruleset.GetAllUnitTypes().Where(it => it.IsUsed(ruleset)))
            .ToList();
    }
}
