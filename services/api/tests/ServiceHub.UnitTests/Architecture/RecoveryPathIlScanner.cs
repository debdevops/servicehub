using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Shared IL-scanning primitives behind every architecture test in this folder that needs to
/// know what a method's compiled body directly calls — <see cref="RecoveryPathCoverageTests"/>
/// (Phase B: every replay/purge caller also calls the ledger and the gate) and
/// <see cref="AIBoundaryArchitectureTests"/> (Phase D §9.4.5: no AI-adjacent type reaches a
/// ledger-write or provider-mutating member). Extracted so both tests run the literal same
/// opcode-decoding/async-resolution logic rather than two copies that could silently drift apart
/// — the exact failure mode Pass 11 (roadmap §9.4.5 Changelog) found and corrected for AI-adjacent
/// type discovery itself.
/// </summary>
internal static class RecoveryPathIlScanner
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodeTable();

    private static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
            {
                table[opCode.Value] = opCode;
            }
        }

        return table;
    }

    /// <summary>
    /// Resolves an async method's IL-invisible wrapper to its compiler-generated state machine's
    /// <c>MoveNext</c> — the method whose body actually contains the awaited calls. Returns
    /// <paramref name="method"/> unchanged if it isn't async.
    /// </summary>
    public static MethodBase ResolveRealMethodBody(MethodBase method)
    {
        var asyncAttribute = method is MethodInfo methodInfo
            ? methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>()
            : null;

        if (asyncAttribute?.StateMachineType is null)
        {
            return method;
        }

        return asyncAttribute.StateMachineType.GetMethod(
            nameof(IAsyncStateMachine.MoveNext),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    /// <summary>
    /// Maps a possibly compiler-generated method (an async state machine's <c>MoveNext</c>) back
    /// to the human-authored (type, method name) pair it was compiled from.
    /// </summary>
    public static (Type Type, string MethodName) ResolveOwningMethod(MethodBase method)
    {
        var declaringType = method.DeclaringType!;

        if (declaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            && typeof(IAsyncStateMachine).IsAssignableFrom(declaringType)
            && declaringType.DeclaringType is not null)
        {
            var match = Regex.Match(declaringType.Name, @"^<(?<name>.+)>d__\d+$");
            var originalName = match.Success ? match.Groups["name"].Value : declaringType.Name;
            return (declaringType.DeclaringType, originalName);
        }

        return (declaringType, method.Name);
    }

    /// <summary>
    /// Reads a method's IL body and yields every method/constructor it directly calls
    /// (<c>call</c>, <c>callvirt</c>, or <c>newobj</c>) — one level, not a transitive call graph.
    /// The opcode-length table is generated from <see cref="OpCodes"/>'s own field values rather
    /// than hand-maintained, so it can't silently drift from the runtime's actual opcode set.
    /// </summary>
    public static IEnumerable<MethodBase> GetDirectlyCalledMethods(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArgs = method.DeclaringType is { IsGenericType: true } declaringType
            ? declaringType.GetGenericArguments()
            : null;
        var methodArgs = method is MethodInfo { IsGenericMethod: true } genericMethod
            ? genericMethod.GetGenericArguments()
            : null;

        var i = 0;
        while (i < il.Length)
        {
            short code = il[i];
            i++;
            if (code == 0xFE)
            {
                code = (short)(0xFE00 | il[i]);
                i++;
            }

            if (!OpCodesByValue.TryGetValue(code, out var opCode))
            {
                // An opcode this table doesn't recognise — stop rather than risk misreading the
                // rest of the stream as a different instruction.
                yield break;
            }

            if (opCode.OperandType == OperandType.InlineSwitch)
            {
                var count = BitConverter.ToInt32(il, i);
                i += 4 + (count * 4);
                continue;
            }

            var operandSize = OperandSize(opCode.OperandType);

            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineTok)
            {
                var token = BitConverter.ToInt32(il, i);
                MethodBase? resolved = null;
                try
                {
                    resolved = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                }
                catch (ArgumentException)
                {
                    // InlineTok also covers field/type tokens; ResolveMethod throws for those —
                    // expected, not a parse failure.
                }

                if (resolved is not null)
                {
                    yield return resolved;
                }
            }

            i += operandSize;
        }
    }

    private static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => throw new NotSupportedException($"Unsupported IL operand type: {operandType}"),
    };
}
