namespace PackForge.Core.Expressions;

/// <summary>Opcodes shared with the native kernel (packforge_eval.cpp). Order must match.</summary>
public enum RpnOp
{
    PushConst = 0, PushVar = 1,
    Add = 2, Sub = 3, Mul = 4, Div = 5, Pow = 6, Neg = 7,
    Sqrt = 8, Abs = 9, Sin = 10, Cos = 11, Tan = 12,
    Exp = 13, Log = 14, Log10 = 15, Floor = 16, Ceil = 17,
    Min = 18, Max = 19,
}

/// <summary>A compiled expression: parallel opcode/operand arrays in RPN order.</summary>
public sealed class RpnProgram
{
    public required int[] Opcodes { get; init; }
    public required double[] Operands { get; init; }
}

/// <summary>
/// Compiles a parsed expression AST into a flat RPN program the native kernel can
/// evaluate. Variable references are resolved to indices via <paramref name="varIndex"/>.
/// </summary>
public static class RpnCompiler
{
    private static readonly Dictionary<string, RpnOp> FunctionOps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sqrt"] = RpnOp.Sqrt, ["abs"] = RpnOp.Abs, ["sin"] = RpnOp.Sin, ["cos"] = RpnOp.Cos,
        ["tan"] = RpnOp.Tan, ["exp"] = RpnOp.Exp, ["log"] = RpnOp.Log, ["log10"] = RpnOp.Log10,
        ["floor"] = RpnOp.Floor, ["ceil"] = RpnOp.Ceil, ["min"] = RpnOp.Min, ["max"] = RpnOp.Max,
        ["pow"] = RpnOp.Pow,
    };

    public static RpnProgram Compile(ExprNode node, IReadOnlyDictionary<string, int> varIndex)
    {
        var ops = new List<int>();
        var operands = new List<double>();
        Emit(node, varIndex, ops, operands);
        return new RpnProgram { Opcodes = [.. ops], Operands = [.. operands] };
    }

    private static void Emit(ExprNode node, IReadOnlyDictionary<string, int> varIndex, List<int> ops, List<double> operands)
    {
        switch (node)
        {
            case NumberNode n:
                Add(ops, operands, RpnOp.PushConst, n.Value);
                break;
            case VariableNode v:
                if (!varIndex.TryGetValue(v.Name, out var idx))
                    throw new KeyNotFoundException($"Unknown symbol '{v.Name}'.");
                Add(ops, operands, RpnOp.PushVar, idx);
                break;
            case UnaryNode u:
                Emit(u.Operand, varIndex, ops, operands);
                Add(ops, operands, RpnOp.Neg, 0);
                break;
            case BinaryNode b:
                Emit(b.Left, varIndex, ops, operands);
                Emit(b.Right, varIndex, ops, operands);
                Add(ops, operands, b.Op switch
                {
                    "+" => RpnOp.Add, "-" => RpnOp.Sub, "*" => RpnOp.Mul,
                    "/" => RpnOp.Div, "^" => RpnOp.Pow,
                    _ => throw new InvalidOperationException($"Unknown operator '{b.Op}'."),
                }, 0);
                break;
            case FunctionNode f:
                foreach (var arg in f.Args)
                    Emit(arg, varIndex, ops, operands);
                if (!FunctionOps.TryGetValue(f.Name, out var fop))
                    throw new KeyNotFoundException($"Unknown function '{f.Name}'.");
                Add(ops, operands, fop, 0);
                break;
            default:
                throw new InvalidOperationException($"Cannot compile node {node.GetType().Name}.");
        }
    }

    private static void Add(List<int> ops, List<double> operands, RpnOp op, double operand)
    {
        ops.Add((int)op);
        operands.Add(operand);
    }
}
