#r "../out/mainline/package/stardewvalleymainline/tools/MainlineGameDataPatcher/Mono.Cecil.dll"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

string Root = Directory.GetCurrentDirectory();
string GameAssemblyPath = Path.Combine(Root, "stardewvalley_steam/Stardew Valley/Stardew Valley.dll");
string StardewMonoGamePath = Path.Combine(Root, "stardewvalley_steam/Stardew Valley/MonoGame.Framework.dll");
string RebuiltMonoGamePath = Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/gamedata/MonoGame.Framework.dll");
string ReportPath = Path.Combine(Root, "build/reports/monogame-api-diff-vs-stardew.md");
string PackagedDotnetPath = Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/dotnet/shared/Microsoft.NETCore.App/6.0.32");

Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);

string Sha256(string path)
{
    using var sha = SHA256.Create();
    using var stream = File.OpenRead(path);
    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
}

AssemblyDefinition ReadAssembly(string path)
{
    if (!File.Exists(path))
        throw new FileNotFoundException(path);

    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(path));
    resolver.AddSearchDirectory(Path.GetDirectoryName(GameAssemblyPath));
    resolver.AddSearchDirectory(Path.GetDirectoryName(RebuiltMonoGamePath));
    resolver.AddSearchDirectory(PackagedDotnetPath);

    return AssemblyDefinition.ReadAssembly(path, new ReaderParameters
    {
        AssemblyResolver = resolver,
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });
}

IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
{
    foreach (var type in module.Types)
    {
        foreach (var nested in AllTypes(type))
            yield return nested;
    }
}

IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes)
    {
        foreach (var child in AllTypes(nested))
            yield return child;
    }
}

bool IsPublicApiType(TypeDefinition type)
{
    return type.IsPublic || type.IsNestedPublic;
}

bool IsPublicApiMethod(MethodDefinition method)
{
    return method.IsPublic;
}

bool IsPublicApiField(FieldDefinition field)
{
    return field.IsPublic;
}

TypeReference BaseTypeReference(TypeReference type)
{
    while (type is TypeSpecification spec)
        type = spec.ElementType;
    return type;
}

string TypeName(TypeReference type)
{
    if (type is GenericParameter genericParameter)
        return genericParameter.Type == GenericParameterType.Method ? $"!!{genericParameter.Position}" : $"!{genericParameter.Position}";
    if (type is GenericInstanceType generic)
        return $"{TypeName(generic.ElementType)}<{string.Join(", ", generic.GenericArguments.Select(TypeName))}>";
    if (type is ArrayType array)
        return $"{TypeName(array.ElementType)}[]";
    if (type is ByReferenceType byRef)
        return $"{TypeName(byRef.ElementType)}&";
    if (type is PointerType pointer)
        return $"{TypeName(pointer.ElementType)}*";
    if (type is OptionalModifierType optional)
        return TypeName(optional.ElementType);
    if (type is RequiredModifierType required)
        return TypeName(required.ElementType);
    return type.FullName;
}

string DeclaringTypeName(TypeReference type)
{
    return TypeName(BaseTypeReference(type));
}

string MethodSignature(MethodReference method)
{
    return $"{method.Name}({string.Join(", ", method.Parameters.Select(p => TypeName(p.ParameterType)))}) -> {TypeName(method.ReturnType)}";
}

string MethodKey(MethodReference method)
{
    return $"{DeclaringTypeName(method.DeclaringType)}::{MethodSignature(method)}";
}

string FieldSignature(FieldReference field)
{
    return $"{field.Name}: {TypeName(field.FieldType)}";
}

string FieldKey(FieldReference field)
{
    return $"{DeclaringTypeName(field.DeclaringType)}::{FieldSignature(field)}";
}

string ScopeAssemblyName(TypeReference type)
{
    type = BaseTypeReference(type);
    if (type.Scope is AssemblyNameReference assemblyName)
        return assemblyName.Name;
    if (type.Scope is ModuleDefinition module)
        return module.Assembly.Name.Name;
    return type.Scope?.Name ?? "";
}

var game = ReadAssembly(GameAssemblyPath);
var stardewMonoGame = ReadAssembly(StardewMonoGamePath);
var rebuiltMonoGame = ReadAssembly(RebuiltMonoGamePath);

var stardewTypes = AllTypes(stardewMonoGame.MainModule).ToDictionary(t => t.FullName);
var rebuiltTypes = AllTypes(rebuiltMonoGame.MainModule).ToDictionary(t => t.FullName);

var stardewPublicTypes = stardewTypes.Values.Where(IsPublicApiType).OrderBy(t => t.FullName).ToList();
var rebuiltTypeNames = new HashSet<string>(rebuiltTypes.Keys);

var missingAllTypes = stardewTypes.Values
    .Where(t => !rebuiltTypeNames.Contains(t.FullName))
    .Select(t => t.FullName)
    .OrderBy(x => x)
    .ToList();

var missingAllMethods = new List<string>();
var missingAllFields = new List<string>();

foreach (var stardewType in stardewTypes.Values.OrderBy(t => t.FullName))
{
    if (!rebuiltTypes.TryGetValue(stardewType.FullName, out var rebuiltType))
        continue;

    var rebuiltMethods = new HashSet<string>(rebuiltType.Methods.Select(MethodSignature));
    var rebuiltFields = new HashSet<string>(rebuiltType.Fields.Select(FieldSignature));

    foreach (var method in stardewType.Methods)
    {
        var signature = MethodSignature(method);
        if (!rebuiltMethods.Contains(signature))
            missingAllMethods.Add($"{stardewType.FullName}::{signature}");
    }

    foreach (var field in stardewType.Fields)
    {
        var signature = FieldSignature(field);
        if (!rebuiltFields.Contains(signature))
            missingAllFields.Add($"{stardewType.FullName}::{signature}");
    }
}

missingAllMethods.Sort(StringComparer.Ordinal);
missingAllFields.Sort(StringComparer.Ordinal);

var missingPublicTypes = stardewPublicTypes
    .Where(t => !rebuiltTypeNames.Contains(t.FullName))
    .Select(t => t.FullName)
    .OrderBy(x => x)
    .ToList();

var missingPublicMethods = new List<string>();
var missingPublicFields = new List<string>();

foreach (var stardewType in stardewPublicTypes)
{
    if (!rebuiltTypes.TryGetValue(stardewType.FullName, out var rebuiltType))
        continue;

    var rebuiltMethods = new HashSet<string>(rebuiltType.Methods.Select(MethodSignature));
    var rebuiltFields = new HashSet<string>(rebuiltType.Fields.Select(FieldSignature));

    foreach (var method in stardewType.Methods.Where(IsPublicApiMethod))
    {
        var signature = MethodSignature(method);
        if (!rebuiltMethods.Contains(signature))
            missingPublicMethods.Add($"{stardewType.FullName}::{signature}");
    }

    foreach (var field in stardewType.Fields.Where(IsPublicApiField))
    {
        var signature = FieldSignature(field);
        if (!rebuiltFields.Contains(signature))
            missingPublicFields.Add($"{stardewType.FullName}::{signature}");
    }
}

missingPublicMethods.Sort(StringComparer.Ordinal);
missingPublicFields.Sort(StringComparer.Ordinal);

var gameMonoGameTypeRefs = game.MainModule.GetTypeReferences()
    .Where(t => ScopeAssemblyName(t) == "MonoGame.Framework")
    .Select(t => DeclaringTypeName(t))
    .Distinct()
    .OrderBy(x => x)
    .ToList();

var missingGameTypeRefs = gameMonoGameTypeRefs
    .Where(t => !rebuiltTypeNames.Contains(t))
    .OrderBy(x => x)
    .ToList();

var rebuiltMethodKeysByType = rebuiltTypes.ToDictionary(
    kvp => kvp.Key,
    kvp => new HashSet<string>(kvp.Value.Methods.Select(MethodSignature)));
var rebuiltFieldKeysByType = rebuiltTypes.ToDictionary(
    kvp => kvp.Key,
    kvp => new HashSet<string>(kvp.Value.Fields.Select(FieldSignature)));

var missingGameMethodRefs = new SortedSet<string>(StringComparer.Ordinal);
var missingGameFieldRefs = new SortedSet<string>(StringComparer.Ordinal);

foreach (var member in game.MainModule.GetMemberReferences())
{
    if (member is MethodReference method && ScopeAssemblyName(method.DeclaringType) == "MonoGame.Framework")
    {
        var typeName = DeclaringTypeName(method.DeclaringType);
        if (!rebuiltMethodKeysByType.TryGetValue(typeName, out var methods) || !methods.Contains(MethodSignature(method)))
            missingGameMethodRefs.Add(MethodKey(method));
    }
    else if (member is FieldReference field && ScopeAssemblyName(field.DeclaringType) == "MonoGame.Framework")
    {
        var typeName = DeclaringTypeName(field.DeclaringType);
        if (!rebuiltFieldKeysByType.TryGetValue(typeName, out var fields) || !fields.Contains(FieldSignature(field)))
            missingGameFieldRefs.Add(FieldKey(field));
    }
}

var sb = new StringBuilder();
sb.AppendLine("# MonoGame API Diff vs Stardew Steam");
sb.AppendLine();
sb.AppendLine($"Report time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
sb.AppendLine();
sb.AppendLine("## Inputs");
sb.AppendLine();
sb.AppendLine("| Name | Path | SHA-256 | Assembly version |");
sb.AppendLine("| --- | --- | --- | --- |");
sb.AppendLine($"| Game assembly | `{Path.GetRelativePath(Root, GameAssemblyPath)}` | `{Sha256(GameAssemblyPath)}` | `{game.Name.Version}` |");
sb.AppendLine($"| Stardew shipped MonoGame | `{Path.GetRelativePath(Root, StardewMonoGamePath)}` | `{Sha256(StardewMonoGamePath)}` | `{stardewMonoGame.Name.Version}` |");
sb.AppendLine($"| Rebuilt MonoGame | `{Path.GetRelativePath(Root, RebuiltMonoGamePath)}` | `{Sha256(RebuiltMonoGamePath)}` | `{rebuiltMonoGame.Name.Version}` |");
sb.AppendLine();
sb.AppendLine("## Naming");
sb.AppendLine();
sb.AppendLine("- `Stardew Valley.dll` is the game assembly. It contains Stardew's game code and references MonoGame APIs.");
sb.AppendLine("- `MonoGame.Framework.dll` is the framework assembly. The Steam game ships one; PortMaster replaces it with the rebuilt ARM64-compatible framework.");
sb.AppendLine("- `Stardew shipped MonoGame` means the original Steam file under `stardewvalley_steam/Stardew Valley/MonoGame.Framework.dll`.");
sb.AppendLine("- `Rebuilt MonoGame` means the currently packaged replacement under `build/out/mainline/package/.../gamedata/MonoGame.Framework.dll`.");
sb.AppendLine();
sb.AppendLine("## Summary");
sb.AppendLine();
sb.AppendLine($"- MonoGame type refs from `Stardew Valley.dll`: {gameMonoGameTypeRefs.Count}");
sb.AppendLine($"- Missing MonoGame type refs required by `Stardew Valley.dll`: {missingGameTypeRefs.Count}");
sb.AppendLine($"- Missing MonoGame method refs required by `Stardew Valley.dll`: {missingGameMethodRefs.Count}");
sb.AppendLine($"- Missing MonoGame field refs required by `Stardew Valley.dll`: {missingGameFieldRefs.Count}");
sb.AppendLine($"- Public types in Stardew shipped MonoGame: {stardewPublicTypes.Count}");
sb.AppendLine($"- Public types from shipped MonoGame missing in the rebuilt framework: {missingPublicTypes.Count}");
sb.AppendLine($"- Public methods from shipped MonoGame missing in the rebuilt framework for shared public types: {missingPublicMethods.Count}");
sb.AppendLine($"- Public fields from shipped MonoGame missing in the rebuilt framework for shared public types: {missingPublicFields.Count}");
sb.AppendLine($"- All types from shipped MonoGame missing in the rebuilt framework: {missingAllTypes.Count}");
sb.AppendLine($"- All methods from shipped MonoGame missing in the rebuilt framework for shared types: {missingAllMethods.Count}");
sb.AppendLine($"- All fields from shipped MonoGame missing in the rebuilt framework for shared types: {missingAllFields.Count}");
sb.AppendLine();
sb.AppendLine("## Actionable Runtime Compatibility Audit");
sb.AppendLine();
sb.AppendLine("This section compares what `Stardew Valley.dll` references against what the rebuilt `MonoGame.Framework.dll` contains. Missing entries here are the highest risk because the game has direct metadata references to them.");
sb.AppendLine();
AppendList(sb, "Missing Type References", missingGameTypeRefs);
AppendList(sb, "Missing Method References", missingGameMethodRefs.ToList());
AppendList(sb, "Missing Field References", missingGameFieldRefs.ToList());
sb.AppendLine("## Broader Public API Diff");
sb.AppendLine();
sb.AppendLine("This section compares the public API surface of Stardew's shipped `MonoGame.Framework.dll` against the rebuilt replacement. Missing entries here are not necessarily used by Stardew, but they show where the shipped DLL exposes more public API than the replacement.");
sb.AppendLine();
AppendList(sb, "Public Types Missing From Rebuilt Framework", missingPublicTypes);
AppendList(sb, "Public Methods Missing From Rebuilt Framework", missingPublicMethods);
AppendList(sb, "Public Fields Missing From Rebuilt Framework", missingPublicFields);
sb.AppendLine("## Full Assembly Metadata Diff");
sb.AppendLine();
sb.AppendLine("This section compares every type/member definition in Stardew's shipped `MonoGame.Framework.dll` against the rebuilt replacement. It includes private/internal implementation details, so missing entries here do not automatically mean Stardew or mods can call them.");
sb.AppendLine();
AppendList(sb, "All Types Missing From Rebuilt Framework", missingAllTypes);
AppendList(sb, "All Methods Missing From Rebuilt Framework", missingAllMethods);
AppendList(sb, "All Fields Missing From Rebuilt Framework", missingAllFields);

File.WriteAllText(ReportPath, sb.ToString());
Console.WriteLine(ReportPath);
Console.WriteLine($"runtime_missing_types={missingGameTypeRefs.Count}");
Console.WriteLine($"runtime_missing_methods={missingGameMethodRefs.Count}");
Console.WriteLine($"runtime_missing_fields={missingGameFieldRefs.Count}");
Console.WriteLine($"public_missing_types={missingPublicTypes.Count}");
Console.WriteLine($"public_missing_methods={missingPublicMethods.Count}");
Console.WriteLine($"public_missing_fields={missingPublicFields.Count}");
Console.WriteLine($"all_missing_types={missingAllTypes.Count}");
Console.WriteLine($"all_missing_methods={missingAllMethods.Count}");
Console.WriteLine($"all_missing_fields={missingAllFields.Count}");

void AppendList(StringBuilder builder, string title, IReadOnlyList<string> items)
{
    builder.AppendLine($"### {title}");
    builder.AppendLine();
    if (items.Count == 0)
    {
        builder.AppendLine("None.");
        builder.AppendLine();
        return;
    }

    builder.AppendLine("```text");
    foreach (var item in items)
        builder.AppendLine(item);
    builder.AppendLine("```");
    builder.AppendLine();
}
