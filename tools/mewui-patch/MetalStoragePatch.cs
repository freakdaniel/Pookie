using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MetalStoragePatch
{
    internal static void Apply(AssemblyDefinition assembly, string? information)
    {
        if (assembly.Name.Version != new Version(0, 10, 1, 0) || information?.Contains("5758c6bfc927d6b1dac64b13f6979fd464e0fcfb", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Review Pookie's Metal texture workaround for this MewVG version.");
        var module = assembly.MainModule;
        var context = module.GetType("Aprillz.MewVG.MNVGcontext");
        var device = context.Fields.Single(f => f.Name == "_device");
        var callbackType = module.ImportReference(typeof(Func<nint, ulong>));
        var callback = new FieldDefinition("PookieScratchStorageMode", FieldAttributes.Public | FieldAttributes.Static, callbackType);
        context.Fields.Add(callback);
        var choose = new MethodDefinition("PookieSelectScratchStorageMode", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.UInt64);
        choose.Parameters.Add(new("device", ParameterAttributes.None, module.TypeSystem.IntPtr));
        context.Methods.Add(choose);
        var invoke = module.ImportReference(typeof(Func<nint, ulong>).GetMethod("Invoke")!);
        var call = Instruction.Create(OpCodes.Ldarg_0);
        var il = choose.Body.GetILProcessor();
        foreach (var instruction in new[]
        {
            Instruction.Create(OpCodes.Ldsfld, callback), Instruction.Create(OpCodes.Dup),
            Instruction.Create(OpCodes.Brtrue, call), Instruction.Create(OpCodes.Pop),
            Instruction.Create(OpCodes.Ldc_I4_2), Instruction.Create(OpCodes.Conv_U8), Instruction.Create(OpCodes.Ret),
            call, Instruction.Create(OpCodes.Callvirt, invoke), Instruction.Create(OpCodes.Ret)
        }) il.Append(instruction);

        // The original "try Memoryless, then Private if nil" aborts inside
        // Metal validation on Intel/AMD, before it can return nil. Select a
        // supported mode before making either scratch texture descriptor.
        foreach (var name in new[] { "EnsureCoverageTexture", "EnsureClipMaskTexture" })
        {
            var method = context.Methods.Single(m => m.Name == name);
            var mode = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldc_I4_3 && i.Next?.OpCode == OpCodes.Conv_I8);
            if (mode.Next.Next?.Operand is not MethodReference { Name: "SendMessage" })
                throw new InvalidOperationException("Unexpected Metal texture storage IL.");
            mode.OpCode = OpCodes.Ldarg_0;
            mode.Next.OpCode = OpCodes.Ldfld; mode.Next.Operand = device;
            method.Body.GetILProcessor().InsertAfter(mode.Next, Instruction.Create(OpCodes.Call, choose));
        }
    }
}
