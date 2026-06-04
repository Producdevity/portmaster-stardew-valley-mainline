#r "../out/mainline/artifacts/MainlineGameDataPatcher/Mono.Cecil.dll"

using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (Args.Count == 0)
{
    Console.Error.WriteLine("Usage: csi build/scripts/inspect-stardew-il.csx <assembly-path>");
    Environment.Exit(1);
}

var assemblyPath = Args[0];
var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
var module = assembly.MainModule;

if (Args.Count > 1)
{
    for (var i = 1; i < Args.Count; i++)
    {
        var spec = Args[i];
        var parts = spec.Split(new[] { "::" }, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            Console.Error.WriteLine($"Invalid method spec: {spec}. Expected Type::Method");
            continue;
        }

        DumpMethods(parts[0], parts[1]);
    }
}
else
{
    DumpMethod("StardewValley.Program", "Main", isStatic: true);
    DumpMethods("StardewValley.GameRunner", ".ctor");
    DumpMethods("StardewValley.Game1", ".ctor");
}

void DumpMethod(string typeName, string methodName, bool isStatic)
{
    var type = module.Types.FirstOrDefault(t => t.FullName == typeName);
    if (type is null)
    {
        Console.WriteLine($"Type not found: {typeName}");
        return;
    }

    var method = type.Methods.FirstOrDefault(m => m.Name == methodName && m.IsStatic == isStatic);
    if (method is null)
    {
        Console.WriteLine($"Method not found: {typeName}::{methodName}");
        return;
    }

    Console.WriteLine($"=== {typeName}::{methodName} ===");
    if (!method.HasBody)
    {
        Console.WriteLine("<no body>");
        return;
    }

    var instructions = method.Body.Instructions;
    for (var i = 0; i < instructions.Count; i++)
    {
        var ins = instructions[i];
        Console.WriteLine($"{i,4}: {ins.Offset:x4} {FormatInstruction(ins)}");
    }

    Console.WriteLine();
}

void DumpMethods(string typeName, string methodName)
{
    var type = module.Types.FirstOrDefault(t => t.FullName == typeName);
    if (type is null)
    {
        Console.WriteLine($"Type not found: {typeName}");
        return;
    }

    var methods = type.Methods.Where(m => m.Name == methodName).ToList();
    if (methods.Count == 0)
    {
        Console.WriteLine($"Method not found: {typeName}::{methodName}");
        return;
    }

    foreach (var method in methods)
    {
        Console.WriteLine($"=== {typeName}::{methodName} ({method.FullName}) ===");
        if (!method.HasBody)
        {
            Console.WriteLine("<no body>");
            Console.WriteLine();
            continue;
        }

        var instructions = method.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            var ins = instructions[i];
            Console.WriteLine($"{i,4}: {ins.Offset:x4} {FormatInstruction(ins)}");
        }

        Console.WriteLine();
    }
}

string FormatInstruction(Instruction ins)
{
    if (ins.Operand is null)
        return ins.OpCode.Name;

    return ins.Operand switch
    {
        MethodReference mr => $"{ins.OpCode.Name} {mr.FullName}",
        FieldReference fr => $"{ins.OpCode.Name} {fr.FullName}",
        TypeReference tr => $"{ins.OpCode.Name} {tr.FullName}",
        Instruction target => $"{ins.OpCode.Name} IL_{target.Offset:x4}",
        Instruction[] targets => $"{ins.OpCode.Name} [{string.Join(", ", targets.Select(t => $"IL_{t.Offset:x4}"))}]",
        string s => $"{ins.OpCode.Name} \"{s}\"",
        _ => $"{ins.OpCode.Name} {ins.Operand}"
    };
}
