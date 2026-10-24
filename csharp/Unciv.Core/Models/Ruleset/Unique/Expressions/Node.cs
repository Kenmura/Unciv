namespace Unciv.Models.Ruleset.Unique.Expressions;

// Ported from core/src/com/unciv/models/ruleset/unique/expressions/Node.kt.
// All ToString() are for debugging only.

internal interface INode
{
    double Eval(GameContext context);
    IReadOnlyList<string> GetErrors(IRulesetParameterView ruleset);
}

internal sealed class NumericConstant(double value) : IConstant
{
    public double Value { get; } = value;
    public override string ToString() => JavaDouble.ToString(Value);
}

internal sealed class UnaryOperation(IUnaryOperator op, INode operand) : INode
{
    public double Eval(GameContext context) => op.Implementation(operand.Eval(context));
    public override string ToString() => $"({op} {operand})";
    public IReadOnlyList<string> GetErrors(IRulesetParameterView ruleset) => operand.GetErrors(ruleset);
}

internal sealed class BinaryOperation(IBinaryOperator op, INode left, INode right) : INode
{
    public double Eval(GameContext context) => op.Implementation(left.Eval(context), right.Eval(context));
    public override string ToString() => $"({left} {op} {right})";

    public IReadOnlyList<string> GetErrors(IRulesetParameterView ruleset)
    {
        var leftErrors = left.GetErrors(ruleset);
        var rightErrors = right.GetErrors(ruleset);
        return [.. leftErrors, .. rightErrors];
    }
}

/// <param name="parameterText">The countable text without the enclosing square brackets.</param>
/// <param name="rulesetInvariantCountable">Most countables can be detected via string pattern.</param>
internal sealed class CountableNode(string parameterText, Countables? rulesetInvariantCountable) : INode, IToken
{
    public double Eval(GameContext context)
    {
        var ruleset = context.GameInfo?.Ruleset;
        if (ruleset == null) return 0.0; // We use "surprised pikachu face" for any unexpected issue so games don't crash

        var countable = GetCountable(ruleset);
        if (countable == null) return 0.0;

        return countable.Eval(parameterText, context) is { } amount ? (double)amount : 0.0;
    }

    private Countables? GetCountable(IRulesetParameterView ruleset) =>
        rulesetInvariantCountable ?? Countables.GetMatching(parameterText, ruleset);

    public IReadOnlyList<string> GetErrors(IRulesetParameterView ruleset)
    {
        if (GetCountable(ruleset) == null)
            return [$"Unknown countable: {parameterText}"];
        return [];
    }

    public override string ToString() => $"[Countable: {parameterText}]";
}

internal sealed class FunctionCall(IFunctionOperator function, IReadOnlyList<INode> arguments) : INode
{
    public double Eval(GameContext context)
    {
        var evaluatedArgs = new List<double>(arguments.Count);
        foreach (var argument in arguments) evaluatedArgs.Add(argument.Eval(context));
        return function.Implementation(evaluatedArgs);
    }

    public IReadOnlyList<string> GetErrors(IRulesetParameterView ruleset)
    {
        var errors = new List<string>();
        foreach (var argument in arguments) errors.AddRange(argument.GetErrors(ruleset));
        return errors;
    }

    public override string ToString() => $"[FunctionCall: {function.Symbol}({string.Join(", ", arguments)})]";
}
