using System.Collections;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/ResourceSupplyList.kt
/// <summary>
/// Container helps aggregating supply and demand of resources, categorized by origin.
/// Kotlin extends ArrayList; this wraps a List so that <see cref="Add(ResourceSupply)"/> keeps its merging semantics.
/// Insertion order is kept (it reaches the UI).
/// </summary>
public class ResourceSupplyList : IEnumerable<ResourceSupplyList.ResourceSupply>
{
    private readonly bool keepZeroAmounts;
    private readonly List<ResourceSupply> items = new(24);

    /// <param name="keepZeroAmounts">If <c>false</c>, entries with amount 0 are eliminated</param>
    public ResourceSupplyList(bool keepZeroAmounts = false)
    {
        this.keepZeroAmounts = keepZeroAmounts;
    }

    /// <summary>
    /// Holds one "data row", Resource and Origin function as keys while Amount is the 'value'.
    /// Kotlin data class: equality over all three fields. No code outside <see cref="ResourceSupplyList"/> should update Amount.
    /// </summary>
    public sealed class ResourceSupply : IEquatable<ResourceSupply>
    {
        public ResourceSupply(TileResource resource, string origin, int amount)
        {
            Resource = resource;
            Origin = origin;
            Amount = amount;
        }

        public TileResource Resource { get; }
        public string Origin { get; }
        public int Amount { get; internal set; }

        public bool IsCityStateOrTradeOrigin() => (Origin == Constants.CityStates || Origin == "Trade") && Amount > 0;

        public override string ToString() => $"{Amount} {Resource.Name} from {Origin}";

        public bool Equals(ResourceSupply? other) =>
            other is not null && Resource.Equals(other.Resource) && Origin == other.Origin && Amount == other.Amount;

        public override bool Equals(object? obj) => Equals(obj as ResourceSupply);

        public override int GetHashCode() => unchecked(((JavaHash.HashCode(Resource.Name) * 31 + JavaHash.HashCode(Origin)) * 31) + Amount);
    }

    public static ResourceSupplyList EmptyList { get; } = new();

    public int Count => items.Count;

    public ResourceSupply this[int index] => items[index];

    /// <summary>Fetch a <see cref="ResourceSupply"/> entry or <c>null</c> if no match found</summary>
    public ResourceSupply? Get(TileResource resource, string origin) =>
        items.FirstOrDefault(it => it.Resource.Name == resource.Name && it.Origin == origin);

    /// <summary>Get the total amount for a resource by name</summary>
    public int SumBy(string resourceName)
    {
        var sum = 0;
        foreach (var it in items)
            if (it.Resource.Name == resourceName) sum = unchecked(sum + it.Amount);
        return sum;
    }

    /// <summary>Get the total amount for a resource</summary>
    public int SumBy(TileResource resource)
    {
        var sum = 0;
        foreach (var it in items)
            if (ReferenceEquals(it.Resource, resource)) sum = unchecked(sum + it.Amount);
        return sum;
    }

    public List<string> Origins() => items.Select(it => it.Origin).Distinct().ToList();

    /// <summary>
    /// Add <paramref name="element"/> unless one for resource/origin already exists, in which case the amounts are added up.
    /// Ensures the list contains no entries with amount 0 unless keepZeroAmounts is on.
    /// </summary>
    /// <returns><c>true</c> if the length of the list changed.</returns>
    public bool Add(ResourceSupply element)
    {
        var existing = Get(element.Resource, element.Origin);
        if (existing != null)
        {
            existing.Amount = unchecked(existing.Amount + element.Amount);
            if (keepZeroAmounts || existing.Amount != 0) return false;
            items.Remove(existing);
        }
        else
        {
            if (!keepZeroAmounts && element.Amount == 0) return false;
            items.Add(element);
        }
        return true;
    }

    /// <summary>Add <paramref name="amount"/> to the entry for resource/origin or create a new one.</summary>
    public void Add(TileResource resource, string origin, int amount = 1) => Add(new ResourceSupply(resource, origin, amount));

    /// <summary>Add all entries from <paramref name="resourceSupplyList"/> to this one.</summary>
    public void Add(ResourceSupplyList resourceSupplyList)
    {
        foreach (var resourceSupply in resourceSupplyList)
            Add(resourceSupply);
    }

    /// <summary>Add entries from a requirements list (as produced by <c>IConstruction.getResourceRequirementsPerTurn</c>), expressing requirement as negative supply.</summary>
    public void SubtractResourceRequirements(Counter<string> resourceRequirements, Ruleset ruleset, string origin)
    {
        foreach (var (resourceName, amount) in resourceRequirements)
        {
            if (!ruleset.TileResources.TryGetValue(resourceName, out var resource)) continue;
            Add(resource, origin, unchecked(-amount));
        }
    }

    /// <summary>Aggregate <paramref name="fromList"/> by resource into this (replacing their origin with <paramref name="newOrigin"/>)</summary>
    /// <returns><c>this</c>, allowing chaining</returns>
    public ResourceSupplyList AddByResource(ResourceSupplyList fromList, string newOrigin)
    {
        foreach (var resourceSupply in fromList)
            Add(resourceSupply.Resource, newOrigin, resourceSupply.Amount);
        return this;
    }

    /// <summary>Same as <see cref="AddByResource"/> but ignores negative amounts</summary>
    public void AddPositiveByResource(ResourceSupplyList fromList, string newOrigin)
    {
        foreach (var resourceSupply in fromList)
            if (resourceSupply.Amount > 0)
                Add(resourceSupply.Resource, newOrigin, resourceSupply.Amount);
    }

    /// <summary>Create a new <see cref="ResourceSupplyList"/> aggregating resources over all origins</summary>
    public ResourceSupplyList SumByResource(string newOrigin) => new ResourceSupplyList(keepZeroAmounts).AddByResource(this, newOrigin);

    /// <summary>Remove all entries from a specific <paramref name="origin"/></summary>
    /// <returns><c>this</c>, allowing chaining</returns>
    public ResourceSupplyList RemoveAll(string origin)
    {
        foreach (var it in items.Where(it => it.Origin == origin).ToList())
            items.Remove(it);
        return this;
    }

    /// <summary>Kotlin <c>MutableList.removeAll(predicate)</c>: remove all entries matching <paramref name="predicate"/></summary>
    public void RemoveAll(Func<ResourceSupply, bool> predicate) => items.RemoveAll(it => predicate(it));

    /// <summary>Multiply each entry's amount by its resource's modifier, if one exists.</summary>
    public void ApplyModifiers(IReadOnlyDictionary<string, float> modifiers)
    {
        if (modifiers.Count == 0) return;
        foreach (var resourceSupply in items)
        {
            if (!modifiers.TryGetValue(resourceSupply.Resource.Name, out var modifier)) continue;
            resourceSupply.Amount = KotlinMath.FloatToInt((float)resourceSupply.Amount * modifier);
        }
    }

    public IEnumerator<ResourceSupply> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
