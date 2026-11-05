using Mono.Cecil;
using Mono.Cecil.Cil;

// Patch our build's private copy, never the NuGet cache. Keep the workaround
// pinned to the inspected implementation and fail if its contract changes.
// The X11 offscreen text cleanup is fixed upstream and is no longer patched.
if (args.Length != 3) throw new ArgumentException("Expected input, output and reference-list paths.");
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
using var resolver = new DefaultAssemblyResolver();
foreach (var directory in File.ReadAllLines(args[2]).Select(Path.GetDirectoryName).Append(Path.GetDirectoryName(typeof(object).Assembly.Location)).OfType<string>().Distinct())
    resolver.AddSearchDirectory(directory);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var information = assembly.CustomAttributes.Single(a => a.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute").ConstructorArguments[0].Value as string;
if (assembly.Name.Version != new Version(0,22,1,0) || information?.Contains("6cda4f9872f7fce8fc980b34f6f9322f5e5a2c39", StringComparison.Ordinal) != true)
    throw new InvalidOperationException("Review Pookie's MewUI extensions for this dependency version.");
if (assembly.Name.Name == "Aprillz.MewUI")
{
    var module = assembly.MainModule;
    // Secondary surfaces have their own viewport. Culling there must not mark
    // a live window node offscreen or discard its cached resources.
    var visual = module.Types.Single(t => t.FullName == "Aprillz.MewUI.Controls.UIElement");
    var captureDepth = new FieldDefinition("PookieCaptureDepth", FieldAttributes.Public | FieldAttributes.Static, module.TypeSystem.Int32);
    captureDepth.CustomAttributes.Add(new CustomAttribute(module.ImportReference(typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!)));
    visual.Fields.Add(captureDepth);
    var render = visual.Methods.Single(m => m.Name == "Render");
    foreach (var call in render.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name is "MarkSubtreeCulled" or "MarkCulledWhileCaching" or "MarkRendered").ToArray())
    {
        var entry = call.Previous;
        if (entry.OpCode != OpCodes.Ldarg_0) throw new InvalidOperationException("Unexpected viewport notification IL.");
        var check = Instruction.Create(OpCodes.Ldsfld, captureDepth);
        foreach (var instruction in render.Body.Instructions)
            if (ReferenceEquals(instruction.Operand, entry)) instruction.Operand = check;
        var il = render.Body.GetILProcessor();
        il.InsertBefore(entry, check);
        il.InsertBefore(entry, Instruction.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(entry, Instruction.Create(OpCodes.Bgt, call.Next));
    }
    // RenderCullViewport is already in surface coordinates in this revision.
    // Transforming it again misplaces opacity surfaces during cropped captures.
    var opacity = visual.Methods.Single(m => m.Name == "BeginOpacityGroup");
    var transformReach = opacity.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "TransformRect");
    if (transformReach.Previous.Operand is not MethodReference { Name: "GetTransform" } || transformReach.Previous.Previous.OpCode != OpCodes.Ldarg_1)
        throw new InvalidOperationException("Unexpected opacity viewport IL.");
    foreach (var instruction in new[] { transformReach.Previous.Previous, transformReach.Previous, transformReach })
    { instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
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
else throw new InvalidOperationException("Unsupported MewUI patch target.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var temporary = output + "." + Environment.ProcessId + ".tmp";
assembly.Write(temporary);
// Avoid making every incremental build recopy the dependency.
if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(File.ReadAllBytes(temporary))) File.Delete(temporary);
else File.Move(temporary, output, overwrite: true);
Console.WriteLine($"MewUI: applied Pookie extension to {assembly.Name.Name} (6cda4f98).");
