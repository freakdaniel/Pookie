using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch our build's private copy, never the NuGet cache. Keep the workaround
// pinned to the inspected implementation and fail if its contract changes.
if (args.Length != 3) throw new ArgumentException("Expected input, output and reference-list paths.");
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
using var resolver = new DefaultAssemblyResolver();
foreach (var directory in File.ReadAllLines(args[2]).Select(Path.GetDirectoryName).Append(Path.GetDirectoryName(typeof(object).Assembly.Location)).OfType<string>().Distinct())
    resolver.AddSearchDirectory(directory);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
if (assembly.Name.Version != new Version(0,22,1,0))
    throw new InvalidOperationException("Review Pookie's MewUI extensions for this dependency version.");
if (assembly.Name.Name == "Aprillz.MewUI")
{
    var module = assembly.MainModule;
    var request = module.Types.Single(t => t.FullName == "Aprillz.MewUI.Text.TextLayoutRequest");
    const string hookName = "PookieTextLayoutTransform";
    if (request.Fields.Any(f => f.Name == hookName)) throw new InvalidOperationException("Text transform is already installed.");
    var delegateType = new GenericInstanceType(module.ImportReference(typeof(Func<,>)));
    delegateType.GenericArguments.Add(request); delegateType.GenericArguments.Add(request);
    var hook = new FieldDefinition(hookName, FieldAttributes.Public | FieldAttributes.Static, delegateType);
    request.Fields.Add(hook);
    var invoke = module.ImportReference(typeof(Func<,>).GetMethod("Invoke")!);
    invoke.DeclaringType = delegateType;
    var engine = module.Types.Single(t => t.FullName == "Aprillz.MewUI.Text.ManagedTextEngine");
    foreach (var name in new[] { "CreateLayout", "GetOrCreateLayout" })
    {
        var method = engine.Methods.Single(m => m.Name == name && m.Parameters[0].ParameterType.FullName == request.FullName);
        var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        var instructions = new List<Instruction> {
            Instruction.Create(OpCodes.Ldsfld, hook), Instruction.Create(OpCodes.Brfalse, first),
            Instruction.Create(OpCodes.Ldsfld, hook), Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Callvirt, invoke), Instruction.Create(OpCodes.Starg, method.Parameters[0]) };
        if (name == "GetOrCreateLayout")
        {
            // Framework measurement helpers use Content caching without an owner.
            // MewUI forbids inline objects in that cache. Keep owner-backed labels
            // cached as usual; use an uncached layout for the ownerless helper.
            var run = module.Types.Single(t => t.FullName == "Aprillz.MewUI.Text.InlineRun");
            var collection = new GenericInstanceType(module.ImportReference(typeof(IReadOnlyCollection<>)));
            collection.GenericArguments.Add(run);
            var count = module.ImportReference(typeof(IReadOnlyCollection<>).GetProperty("Count")!.GetMethod!);
            count.DeclaringType = collection;
            var getter = request.Properties.Single(p => p.Name == "Inlines").GetMethod;
            instructions.AddRange([
                Instruction.Create(OpCodes.Ldarg_2), Instruction.Create(OpCodes.Brtrue, first),
                Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Callvirt, getter),
                Instruction.Create(OpCodes.Callvirt, count), Instruction.Create(OpCodes.Brfalse, first),
                Instruction.Create(OpCodes.Ldc_I4_2), Instruction.Create(OpCodes.Starg, method.Parameters[1]) ]);
        }
        foreach (var instruction in instructions) il.InsertBefore(first, instruction);
        method.Body.MaxStackSize = Math.Max(2, method.Body.MaxStackSize);
    }
}
else if (assembly.Name.Name == "Aprillz.MewUI.Backend.MewVG.X11")
{
    var context = assembly.MainModule.Types.Single(t => t.FullName == "Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsContext");
    var session = context.NestedTypes.Single(t => t.Name == "X11OffscreenFrameSession");
    var endFrame = session.Methods.Single(m => m.Name == "EndFrame");
    var cache = session.Methods.Single(m => m.Name == "get_TextCache");
    var release = assembly.MainModule.Types.Single(t => t.Name == "MewVGTextCache").Methods.Single(m => m.Name == "ReleasePendingDeletes");
    if (endFrame.Body.Instructions.Any(i => i.Operand is MethodReference m && m.FullName == release.FullName))
        throw new InvalidOperationException("MewUI already cleans offscreen text caches; remove this workaround after verification.");
    // EndFrame is entered after the offscreen MewVG flush. Match its existing
    // image-release call, and drain text deletes/reset transient slots immediately
    // afterwards, while the owning GL context is still current.
    var anchor = endFrame.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "ReleasePendingImagesForVg");
    if (anchor.Next?.OpCode != OpCodes.Pop) throw new InvalidOperationException("Unexpected offscreen cleanup IL.");
    var il = endFrame.Body.GetILProcessor();
    var cursor = anchor.Next;
    foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, cache), Instruction.Create(OpCodes.Callvirt, release) })
    {
        il.InsertAfter(cursor, instruction); cursor = instruction;
    }
    endFrame.Body.MaxStackSize = Math.Max(2, endFrame.Body.MaxStackSize);
}
else throw new InvalidOperationException("Unsupported MewUI patch target.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var temporary = output + "." + Environment.ProcessId + ".tmp";
assembly.Write(temporary);
// Avoid making every incremental build recopy the dependency.
if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(File.ReadAllBytes(temporary))) File.Delete(temporary);
else File.Move(temporary, output, overwrite: true);
Console.WriteLine($"MewUI: applied Pookie extension to {assembly.Name.Name} (0.22.1).");
