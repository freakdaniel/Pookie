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
if (assembly.Name.Name != "Aprillz.MewUI.Backend.MewVG.X11" || assembly.Name.Version != new Version(0,22,1,0))
    throw new InvalidOperationException("Review the offscreen text-cache workaround for this MewUI version before building.");
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
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var temporary = output + "." + Environment.ProcessId + ".tmp";
assembly.Write(temporary);
// Avoid making every incremental build recopy the dependency.
if (File.Exists(output) && File.ReadAllBytes(output).AsSpan().SequenceEqual(File.ReadAllBytes(temporary))) File.Delete(temporary);
else File.Move(temporary, output, overwrite: true);
Console.WriteLine("MewUI X11: enabled per-frame offscreen text-cache cleanup (0.22.1).");
