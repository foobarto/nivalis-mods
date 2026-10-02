using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Cheeze.Managed;

// Resolve the adapter's compiled member references against installed assemblies
// before detouring any game methods. This checks bindings, not native semantics.
public static class ManagedApiCompatibility
{
    public static void Validate(Assembly assembly)
    {
        var codes = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.GetValue(null) is OpCode code) codes[code.Value] = code;
        foreach (var type in assembly.GetTypes())
        {
            var methods = new List<MethodBase>(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
            methods.AddRange(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            foreach (var method in methods)
            {
                var bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null) continue;
                int position = 0;
                while (position < bytes.Length)
                {
                    short opcode = bytes[position++];
                    if (opcode == 0xFE) opcode = unchecked((short)(0xFE00 | bytes[position++]));
                    var code = codes[opcode];
                    int size = code.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, position) * 4),
                        _ => 4
                    };
                    if (size < 0 || position + size > bytes.Length) throw new BadImageFormatException("Invalid adapter IL.");
                    if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType)
                        method.Module.ResolveMember(BitConverter.ToInt32(bytes, position), type.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                    position += size;
                }
            }
        }
    }
}
