using System.Runtime.InteropServices;

namespace PackForge.Core.Expressions;

/// <summary>P/Invoke bindings to the native math kernel (packforge_eval).</summary>
public static partial class NativeMathKernel
{
    private const string Lib = "packforge_eval";

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial double pf_eval(int[] opcodes, double[] operands, int stepCount, double[] vars, int varCount, out int error);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int pf_probe();

    private static readonly Lazy<bool> Available = new(() =>
    {
        try { return pf_probe() == 0x5046; }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>True when the native library loaded and answered the probe.</summary>
    public static bool IsAvailable => Available.Value;

    /// <summary>Evaluate one compiled RPN program against the given variable values.</summary>
    public static double Evaluate(RpnProgram program, double[] vars)
    {
        var result = pf_eval(program.Opcodes, program.Operands, program.Opcodes.Length, vars, vars.Length, out var error);
        if (error != 0)
            throw new InvalidOperationException($"Native kernel error {error} evaluating expression.");
        return result;
    }
}

/// <summary>
/// Evaluates a model with the native C++ kernel — parsing/validation stays in C#,
/// the numeric core runs in C++. Falls back to the managed evaluator when the
/// native library isn't present (e.g. CI without the built DLL).
/// </summary>
public static class NativeModelEvaluator
{
    public static bool IsAvailable => NativeMathKernel.IsAvailable;

    public static Dictionary<string, double> Evaluate(Models.ModelDefinition model)
    {
        if (!NativeMathKernel.IsAvailable)
            return Models.ModelEvaluator.Evaluate(model);

        var names = new List<string>(model.Parameters.Keys);
        var values = new List<double>(model.Parameters.Values);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
            index[names[i]] = i;

        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var expr in model.Expressions)
        {
            var ast = ExpressionParser.Parse(expr.Formula);
            var program = RpnCompiler.Compile(ast, index);
            var value = NativeMathKernel.Evaluate(program, [.. values]);

            index[expr.Name] = names.Count;
            names.Add(expr.Name);
            values.Add(value);
            results[expr.Name] = value;
        }
        return results;
    }
}
