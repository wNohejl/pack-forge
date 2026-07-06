// PackForge native math kernel.
//
// The C# side owns parsing/validation (the platform); this C++ component is the
// numeric engine — it evaluates a compiled RPN program over a set of variable
// values. Mirrors the C#/C++ split of a Blazor platform over a C++ compute core.
//
// ABI (C, for P/Invoke): evaluate one RPN program, return the result (NaN on error).
//   opcodes[i]  : instruction (see Op below)
//   operands[i] : for PushConst = the constant; for PushVar = the variable index
//   stepCount   : number of instructions
//   vars/varCount : variable values, indexed by PushVar operand
//   errorOut    : 0 ok, 1 stack error, 2 unknown opcode

#include <cmath>
#include <cstdint>
#include <vector>

#if defined(_WIN32)
#define PF_EXPORT extern "C" __declspec(dllexport)
#else
#define PF_EXPORT extern "C" __attribute__((visibility("default")))
#endif

enum Op : int {
    PushConst = 0, PushVar = 1,
    Add = 2, Sub = 3, Mul = 4, Div = 5, Pow = 6, Neg = 7,
    Sqrt = 8, Abs = 9, Sin = 10, Cos = 11, Tan = 12,
    Exp = 13, Log = 14, Log10 = 15, Floor = 16, Ceil = 17,
    Min = 18, Max = 19
};

PF_EXPORT double pf_eval(const int* opcodes, const double* operands, int stepCount,
                         const double* vars, int varCount, int* errorOut) {
    std::vector<double> stack;
    stack.reserve(stepCount > 0 ? stepCount : 16);
    if (errorOut) *errorOut = 0;

    auto fail = [&](int code) -> double {
        if (errorOut) *errorOut = code;
        return std::nan("");
    };

    for (int i = 0; i < stepCount; ++i) {
        const int op = opcodes[i];
        switch (op) {
            case PushConst: stack.push_back(operands[i]); break;
            case PushVar: {
                const int idx = static_cast<int>(operands[i]);
                if (idx < 0 || idx >= varCount) return fail(1);
                stack.push_back(vars[idx]);
                break;
            }
            case Neg:
                if (stack.empty()) return fail(1);
                stack.back() = -stack.back();
                break;
            case Sqrt: case Abs: case Sin: case Cos: case Tan:
            case Exp: case Log: case Log10: case Floor: case Ceil: {
                if (stack.empty()) return fail(1);
                double a = stack.back();
                switch (op) {
                    case Sqrt: a = std::sqrt(a); break;
                    case Abs: a = std::fabs(a); break;
                    case Sin: a = std::sin(a); break;
                    case Cos: a = std::cos(a); break;
                    case Tan: a = std::tan(a); break;
                    case Exp: a = std::exp(a); break;
                    case Log: a = std::log(a); break;
                    case Log10: a = std::log10(a); break;
                    case Floor: a = std::floor(a); break;
                    case Ceil: a = std::ceil(a); break;
                }
                stack.back() = a;
                break;
            }
            case Add: case Sub: case Mul: case Div: case Pow: case Min: case Max: {
                if (stack.size() < 2) return fail(1);
                double b = stack.back(); stack.pop_back();
                double a = stack.back();
                switch (op) {
                    case Add: a = a + b; break;
                    case Sub: a = a - b; break;
                    case Mul: a = a * b; break;
                    case Div: a = a / b; break;
                    case Pow: a = std::pow(a, b); break;
                    case Min: a = a < b ? a : b; break;
                    case Max: a = a > b ? a : b; break;
                }
                stack.back() = a;
                break;
            }
            default:
                return fail(2);
        }
    }

    if (stack.size() != 1) return fail(1);
    return stack.back();
}

// Probe used by the managed side to confirm the kernel loaded and is callable.
PF_EXPORT int pf_probe() { return 0x5046; } // 'P','F'
