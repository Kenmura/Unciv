using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/ui/screens/pickerscreens/PromotionTree.kt (T126). Deviations: the MapUnitView constructor is not ported (UI
// view class); the collator is the one in the GameInfo's GameClientState (Kotlin: GUI.getSettings().getCollatorFromLocale()).

public class PromotionTree
{
    private readonly List<AvailablePromotion> availablePromotions;
    /// <summary>Whether the unit has enough XP to buy the next <c>count</c> non-free promotions - queried live</summary>
    private readonly Func<int, bool> canAffordPromotions;
    private readonly IComparer<string> collator;

    public PromotionTree(List<AvailablePromotion> availablePromotions, Func<int, bool> canAffordPromotions, IComparer<string> collator)
    {
        this.availablePromotions = availablePromotions;
        this.canAffordPromotions = canAffordPromotions;
        this.collator = collator;
        Update();
    }

    public PromotionTree(MapUnit unit)
        : this(unit.Promotions.GetPromotionTreeCandidates(), unit.Promotions.CanAffordPromotions, unit.Civ.GameInfo.Collator)
    {
    }

    /// <summary>Ordered set of Promotions to show - by Json column/row and translated name</summary>
    public LinkedHashSet<Promotion> PossiblePromotions { get; private set; } = [];
    /// <summary>Ordered map, key is the Promotion name, same order as <see cref="PossiblePromotions"/></summary>
    private LinkedHashMap<string, PromotionNode> nodes = new();

    public class PromotionNode
    {
        public PromotionNode(Promotion promotion, bool isAdopted)
        {
            Promotion = promotion;
            IsAdopted = isAdopted;
            var splitName = Promotion.GetBaseNameAndLevel(promotion.Name);
            Level = splitName.Level;
            BaseName = splitName.BasePromotionName;
        }

        public Promotion Promotion { get; }
        public bool IsAdopted { get; }

        /// <summary>How many prerequisite steps are needed to reach a <see cref="IsRoot"/> promotion</summary>
        public int Depth { get; set; } = int.MinValue;
        /// <summary>How many unit-promoting steps are needed to reach this node</summary>
        public int DistanceToAdopted { get; set; } = int.MaxValue;

        /// <summary>The nodes for direct prerequisites of this one (unordered). Not necessarily all prerequisites of the node's promotion - see <see cref="Unreachable"/></summary>
        public LinkedHashSet<PromotionNode> Parents { get; } = [];
        /// <summary>Follow this to get an unambiguous path to a root</summary>
        public PromotionNode? PreferredParent { get; set; }
        /// <summary>All nodes having this one as direct prerequisite - must preserve order as UI uses it</summary>
        public LinkedHashSet<PromotionNode> Children { get; } = [];

        /// <summary>Off if there is only one "best" path of equal cost to adopt this node's promotion</summary>
        public bool PathIsAmbiguous { get; set; }
        /// <summary>On for promotions having unavailable prerequisites, or currently disabled by an OnlyAvailable unique.</summary>
        public bool Unreachable { get; set; }

        /// <summary>Name of this node's promotion with level suffixes removed, and [] brackets removed</summary>
        public string BaseName { get; }
        /// <summary>"Level" of this node's promotion (e.g. Drill I: 1, Drill III: 3 - 0 for promotions without such a suffix)</summary>
        public int Level { get; }
        /// <summary>How many levels of this promotion there are below (including this), minimum 1 (Drill I: 3 / Drill III: 1)</summary>
        public int Levels { get; set; } = 1;

        /// <summary><c>true</c> if this node's promotion has no prerequisites</summary>
        public bool IsRoot => Parents.Count == 0;

        public override string ToString() => Promotion.Name;
    }

    public void Update()
    {
        // The following sort is mostly redundant with our vanilla rulesets.
        // Still, want to make sure processing left to right, top to bottom will be usable.
        // Remember to make sure row=0/col=0 stays on top while those without explicit pos go to the end
        // Also remember the names are historical, row means column on our current screen design.
        PossiblePromotions = new LinkedHashSet<Promotion>(availablePromotions
            .Select(it => it.Promotion)
            .OrderBy(it => it.Row < 0 ? int.MaxValue : it.Row == 0 ? unchecked(int.MinValue + it.Column) : it.Column)
            .ThenBy(it => it.Row)
            .ThenBy(it => it.Name.Tr(hideIcons: true), collator));

        var adopted = availablePromotions.Where(it => it.IsAdopted).Select(it => it.Promotion.Name).ToHashSet(StringComparer.Ordinal);
        var blockedByUniques = availablePromotions.Where(it => it.IsBlockedByUniques).Select(it => it.Promotion.Name).ToHashSet(StringComparer.Ordinal);

        // Create incomplete node objects
        nodes = new LinkedHashMap<string, PromotionNode>(StringComparer.Ordinal);
        foreach (var promotion in PossiblePromotions)
            nodes.Put(promotion.Name, new PromotionNode(promotion, adopted.Contains(promotion.Name)));

        // Fill parent/child relations, ignoring prerequisites not in possiblePromotions
        foreach (var node in nodes.Values)
        {
            if (DetectLoop(node)) continue;
            foreach (var prerequisite in node.Promotion.Prerequisites)
            {
                if (!nodes.TryGetValue(prerequisite, out var parent)) continue;
                node.Parents.Add(parent);
                parent.Children.Add(node);
                if (node.Level > 0 && node.BaseName == parent.BaseName)
                    parent.Levels++;
            }
        }

        // Determine unreachable / disabled nodes
        foreach (var node in nodes.Values)
        {
            // defensive - I don't know how to provoke the situation, but if it ever occurs, disallow choosing that promotion
            if (node.Promotion.Prerequisites.Count != 0 && node.Parents.Count == 0)
                node.Unreachable = true;

            if (blockedByUniques.Contains(node.Promotion.Name)) node.Unreachable = true;
        }

        // Calculate depth and distanceToAdopted - nonrecursively, shallows first.
        // Also determine preferredParent / pathIsAmbiguous by weighing distanceToAdopted
        foreach (var node in AllRoots().ToList())
        {
            node.Depth = 0;
            node.DistanceToAdopted = node.IsAdopted ? 0
                : node.Unreachable ? int.MaxValue : 1;
        }
        for (var depth = 0; depth <= 99; depth++)
        {
            var complete = true;
            foreach (var node in nodes.Values)
            {
                if (node.Depth == int.MinValue)
                {
                    complete = false;
                    continue;
                }
                if (node.Depth != depth) continue;
                foreach (var child in node.Children)
                {
                    var distance = child.IsAdopted ? 0
                        : node.DistanceToAdopted == int.MaxValue ? int.MaxValue
                        : child.Unreachable ? int.MaxValue
                        : node.DistanceToAdopted + 1;
                    if (child.Depth == int.MinValue)
                    {
                        // "New" node / first reached
                    }
                    else if (child.DistanceToAdopted < distance)
                    {
                        continue; // Already reached a better way
                    }
                    else if (child.DistanceToAdopted == distance)
                    {
                        // Already reached same distance
                        child.PathIsAmbiguous = true;
                        child.PreferredParent = null;
                        continue;
                    }
                    // else: Already reached, but a worse way - overwrite fully
                    child.Depth = depth + 1;
                    child.DistanceToAdopted = distance;
                    child.PathIsAmbiguous = node.PathIsAmbiguous;
                    child.PreferredParent = node.PathIsAmbiguous ? null : node;
                }
            }
            if (complete) break;
        }
    }

    public IEnumerable<PromotionNode> AllNodes() => nodes.Values;
    public IEnumerable<PromotionNode> AllRoots() => AllNodes().Where(it => it.IsRoot);

    private bool DetectLoop(PromotionNode node)
    {
        var loopCheck = new HashSet<PromotionNode>(nodes.Count);
        return DetectRecursive(node, 0, loopCheck);

        static bool DetectRecursive(PromotionNode node, int level, HashSet<PromotionNode> loopCheck)
        {
            if (level > 99) return true;
            if (loopCheck.Contains(node)) return true;
            loopCheck.Add(node);
            foreach (var parent in node.Parents)
            {
                if (DetectRecursive(parent, level + 1, loopCheck)) return true;
            }
            return false;
        }
    }

    public PromotionNode? GetNode(Promotion promotion) => nodes.TryGetValue(promotion.Name, out var node) ? node : null;

    private PromotionNode? GetReachableNode(Promotion promotion) =>
        nodes.TryGetValue(promotion.Name, out var node) && node.DistanceToAdopted != int.MaxValue ? node : null;

    public bool CanBuyUpTo(Promotion promotion)
    {
        var node = GetReachableNode(promotion);
        if (node == null) return false;
        if (node.IsAdopted) return false;
        // Free promotions don't consume XP; only count non-free promotions in the path
        var nonFreeCount = GetPathTo(promotion).Count(it => !((IHasUniques)it).HasUnique(UniqueType.FreePromotion));
        return canAffordPromotions(nonFreeCount);
    }

    public List<Promotion> GetPathTo(Promotion promotion)
    {
        var node = GetReachableNode(promotion);
        if (node == null) return [];
        var result = new List<Promotion> { node.Promotion };
        while (true)
        {
            var parent = node.PreferredParent;
            if (parent == null) break;
            node = parent;
            if (node.IsAdopted) break;
            result.Add(node.Promotion);
        }
        result.Reverse();
        return result;
    }

    // These exist to allow future optimization - this is safe, but more than actually needed
    public int GetMaxRows() => nodes.Count;
    public int GetMaxColumns() => nodes.Values.Select(it => Math.Max(it.Promotion.Row, it.Depth) + it.Levels).DefaultIfEmpty(0).Max();
}
