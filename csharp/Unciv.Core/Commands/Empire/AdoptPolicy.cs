using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Commands.Empire;

/// <summary>
/// Adopt a policy or open a policy branch. Kotlin: <c>PolicyPickerScreen</c> <c>Policy.isPickable</c> (not adopted, not a branch-complete
/// policy, <c>isAdoptable</c>, <c>canAdoptPolicy</c>), then <c>policies.adopt(policy)</c>; the engine decides between a free policy and
/// the culture cost. Branch completion bonuses fire inside <c>adopt</c>.
/// B013: also the way to choose an ideology (name an ideology branch; it costs nothing) and to adopt a tenet (paid with a free tenet or culture).
/// </summary>
[CommandType("adopt-policy")]
public sealed class AdoptPolicy : GameCommand
{
    public string Policy { get; init; } = "";

    private static Policy? Find(CommandContext context, string name)
    {
        var ruleset = context.Game.Ruleset;
        if (ruleset.Policies.TryGetValue(name, out var policy)) return policy;
        return ruleset.PolicyBranches.TryGetValue(name, out var branch) ? branch : null;
    }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        var policy = Find(context, Policy);
        if (policy == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no policy \"{Policy}\"");
        var policies = actor.Policies;
        if (policies.IsAdopted(policy.Name) || policy.PolicyBranchType == Models.Ruleset.Policy.PolicyBranchTypes.BranchComplete
            || !policies.CanAdoptPolicy(policy))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Policy} cannot be adopted now");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Actor.Policies.Adopt(Find(context, Policy).NotNull());
        return [];
    }
}
