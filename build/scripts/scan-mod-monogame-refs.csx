#r "../out/mainline/package/stardewvalleymainline/tools/MainlineGameDataPatcher/Mono.Cecil.dll"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

string Root = Directory.GetCurrentDirectory();
string StardewMonoGamePath = Path.Combine(Root, "stardewvalley_steam/Stardew Valley/MonoGame.Framework.dll");
string RebuiltMonoGamePath = FirstExisting(
    Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/gamedata/MonoGame.Framework.dll"),
    Path.Combine(Root, "build/out/mainline/artifacts/MonoGame.Framework.dll"),
    Path.Combine(Root, "build/out/mainline/release-package/stardewvalleymainline/overrides/gamedata/MonoGame.Framework.dll")
);
string PackagedGameDataPath = Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/gamedata");
string PackagedDotnetPath = Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/dotnet/shared/Microsoft.NETCore.App/6.0.32");
string ReportPath = Path.Combine(Root, "build/reports/smapi-mod-monogame-ref-scan.md");

var explicitScanRoots = new List<string>();
for (int i = 0; i < Args.Count; i++)
{
    var arg = Args[i];
    if (arg == "--report" && i + 1 < Args.Count)
    {
        ReportPath = Path.GetFullPath(Args[++i]);
        continue;
    }
    if (arg.StartsWith("--report=", StringComparison.Ordinal))
    {
        ReportPath = Path.GetFullPath(arg.Substring("--report=".Length));
        continue;
    }

    explicitScanRoots.Add(Path.GetFullPath(arg));
}

var defaultScanRoots = new[]
{
    Path.Combine(Root, "portmaster_stardewvalley_mainline/stardewvalleymainline/Mods"),
    Path.Combine(Root, "build/out/mainline/package/stardewvalleymainline/Mods")
};

IEnumerable<string> scanRootCandidates = explicitScanRoots.Count > 0 ? explicitScanRoots : defaultScanRoots;
var scanRoots = scanRootCandidates
    .Where(path => !string.IsNullOrWhiteSpace(path))
    .Select(Path.GetFullPath)
    .Distinct(StringComparer.Ordinal)
    .ToList();

Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);

if (!File.Exists(StardewMonoGamePath))
    throw new FileNotFoundException(StardewMonoGamePath);
if (!File.Exists(RebuiltMonoGamePath))
    throw new FileNotFoundException(RebuiltMonoGamePath);

var stardewMonoGame = ReadAssembly(StardewMonoGamePath, Enumerable.Empty<string>());
var rebuiltMonoGame = ReadAssembly(RebuiltMonoGamePath, Enumerable.Empty<string>());

var stardewTypes = AllTypes(stardewMonoGame.MainModule).ToDictionary(t => t.FullName);
var rebuiltTypes = AllTypes(rebuiltMonoGame.MainModule).ToDictionary(t => t.FullName);
var rebuiltTypeNames = new HashSet<string>(rebuiltTypes.Keys);
var stardewPublicTypes = stardewTypes.Values.Where(IsPublicApiType).OrderBy(t => t.FullName).ToList();

var missingPublicTypes = new SortedSet<string>(
    stardewPublicTypes
        .Where(t => !rebuiltTypeNames.Contains(t.FullName))
        .Select(t => t.FullName),
    StringComparer.Ordinal);

var missingPublicMethods = new SortedSet<string>(StringComparer.Ordinal);
var missingPublicFields = new SortedSet<string>(StringComparer.Ordinal);

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

var existingScanRoots = scanRoots.Where(Directory.Exists).ToList();
var missingScanRoots = scanRoots.Where(path => !Directory.Exists(path)).ToList();

var searchDirectories = new SortedSet<string>(StringComparer.Ordinal);
AddSearchDirectory(searchDirectories, Path.GetDirectoryName(StardewMonoGamePath));
AddSearchDirectory(searchDirectories, Path.GetDirectoryName(RebuiltMonoGamePath));
AddSearchDirectory(searchDirectories, PackagedGameDataPath);
AddSearchDirectory(searchDirectories, Path.Combine(PackagedGameDataPath, "smapi-internal"));
AddSearchDirectory(searchDirectories, PackagedDotnetPath);

var dllPaths = existingScanRoots
    .SelectMany(root => Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
    .Where(path => !IsAppleSidecar(path))
    .OrderBy(path => path, StringComparer.Ordinal)
    .ToList();

foreach (var path in dllPaths)
    AddSearchDirectory(searchDirectories, Path.GetDirectoryName(path));

var results = new List<ScanResult>();
foreach (var dllPath in dllPaths)
    results.Add(ScanModAssembly(dllPath, searchDirectories));

WriteReport();
Console.WriteLine(ReportPath);
Console.WriteLine($"mods_scanned={results.Count}");
Console.WriteLine($"mods_with_missing_refs={results.Count(r => r.HasMissingRefs)}");
Console.WriteLine($"scan_errors={results.Count(r => r.Error != null)}");

ScanResult ScanModAssembly(string dllPath, IEnumerable<string> searchDirs)
{
    var result = new ScanResult { Path = dllPath };

    try
    {
        var assembly = ReadAssembly(dllPath, searchDirs);

        foreach (var typeRef in assembly.MainModule.GetTypeReferences())
        {
            if (ScopeAssemblyName(typeRef) != "MonoGame.Framework")
                continue;

            var typeName = DeclaringTypeName(typeRef);
            result.MonoGameTypeRefs.Add(typeName);
            if (missingPublicTypes.Contains(typeName))
                result.MissingTypeRefs.Add(typeName);
        }

        foreach (var member in assembly.MainModule.GetMemberReferences())
        {
            if (member is MethodReference method && ScopeAssemblyName(method.DeclaringType) == "MonoGame.Framework")
            {
                var key = MethodKey(method);
                result.MonoGameMemberRefs.Add(key);
                if (missingPublicMethods.Contains(key))
                    result.MissingMethodRefs.Add(key);
            }
            else if (member is FieldReference field && ScopeAssemblyName(field.DeclaringType) == "MonoGame.Framework")
            {
                var key = FieldKey(field);
                result.MonoGameMemberRefs.Add(key);
                if (missingPublicFields.Contains(key))
                    result.MissingFieldRefs.Add(key);
            }
        }
    }
    catch (Exception ex)
    {
        result.Error = $"{ex.GetType().Name}: {ex.Message}";
    }

    return result;
}

AssemblyDefinition ReadAssembly(string path, IEnumerable<string> extraSearchDirs)
{
    var resolver = new DefaultAssemblyResolver();

    foreach (var dir in extraSearchDirs)
        AddResolverDirectory(resolver, dir);

    AddResolverDirectory(resolver, Path.GetDirectoryName(path));
    AddResolverDirectory(resolver, Path.GetDirectoryName(StardewMonoGamePath));
    AddResolverDirectory(resolver, Path.GetDirectoryName(RebuiltMonoGamePath));
    AddResolverDirectory(resolver, PackagedGameDataPath);
    AddResolverDirectory(resolver, Path.Combine(PackagedGameDataPath, "smapi-internal"));
    AddResolverDirectory(resolver, PackagedDotnetPath);

    return AssemblyDefinition.ReadAssembly(path, new ReaderParameters
    {
        AssemblyResolver = resolver,
        ReadingMode = ReadingMode.Deferred,
        ReadSymbols = false
    });
}

void WriteReport()
{
    var sb = new StringBuilder();
    sb.AppendLine("# SMAPI Mod MonoGame Reference Scan");
    sb.AppendLine();
    sb.AppendLine($"Report time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine();
    sb.AppendLine("## Inputs");
    sb.AppendLine();
    sb.AppendLine($"- Stardew shipped MonoGame: `{Relative(StardewMonoGamePath)}`");
    sb.AppendLine($"- Rebuilt MonoGame: `{Relative(RebuiltMonoGamePath)}`");
    sb.AppendLine($"- Report path: `{Relative(ReportPath)}`");
    sb.AppendLine();
    sb.AppendLine("## Scan Roots");
    sb.AppendLine();
    AppendPathList(sb, "Scanned", existingScanRoots);
    AppendPathList(sb, "Not Present", missingScanRoots);
    sb.AppendLine("## Missing Public MonoGame Surface");
    sb.AppendLine();
    sb.AppendLine($"- Missing public types: {missingPublicTypes.Count}");
    sb.AppendLine($"- Missing public methods: {missingPublicMethods.Count}");
    sb.AppendLine($"- Missing public fields: {missingPublicFields.Count}");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    sb.AppendLine();
    sb.AppendLine($"- DLLs scanned: {results.Count}");
    sb.AppendLine($"- DLLs with any MonoGame references: {results.Count(r => r.HasMonoGameRefs)}");
    sb.AppendLine($"- DLLs with missing public MonoGame references: {results.Count(r => r.HasMissingRefs)}");
    sb.AppendLine($"- DLLs that could not be read: {results.Count(r => r.Error != null)}");
    sb.AppendLine();

    var missingResults = results.Where(r => r.HasMissingRefs).ToList();
    sb.AppendLine("## Missing Reference Findings");
    sb.AppendLine();
    if (missingResults.Count == 0)
    {
        sb.AppendLine("No scanned mod DLL directly references a currently missing public MonoGame type, method, or field.");
        sb.AppendLine();
    }
    else
    {
        foreach (var result in missingResults)
        {
            sb.AppendLine($"### `{Relative(result.Path)}`");
            sb.AppendLine();
            AppendSet(sb, "Missing Type References", result.MissingTypeRefs);
            AppendSet(sb, "Missing Method References", result.MissingMethodRefs);
            AppendSet(sb, "Missing Field References", result.MissingFieldRefs);
        }
    }

    var errors = results.Where(r => r.Error != null).ToList();
    sb.AppendLine("## Read Errors");
    sb.AppendLine();
    if (errors.Count == 0)
    {
        sb.AppendLine("None.");
        sb.AppendLine();
    }
    else
    {
        sb.AppendLine("```text");
        foreach (var result in errors)
            sb.AppendLine($"{Relative(result.Path)}: {result.Error}");
        sb.AppendLine("```");
        sb.AppendLine();
    }

    sb.AppendLine("## All Scanned DLLs");
    sb.AppendLine();
    if (results.Count == 0)
    {
        sb.AppendLine("None.");
        sb.AppendLine();
    }
    else
    {
        sb.AppendLine("| DLL | MonoGame type refs | MonoGame member refs | Missing refs |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            var missingCount = result.MissingTypeRefs.Count + result.MissingMethodRefs.Count + result.MissingFieldRefs.Count;
            sb.AppendLine($"| `{Relative(result.Path)}` | {result.MonoGameTypeRefs.Count} | {result.MonoGameMemberRefs.Count} | {missingCount} |");
        }
        sb.AppendLine();
    }

    sb.AppendLine("## Limits");
    sb.AppendLine();
    sb.AppendLine("- This detects static CLR metadata references in mod DLLs.");
    sb.AppendLine("- It does not detect reflection-only calls, Harmony patches that build member names dynamically, native libraries, or APIs reached only after a mod downloads/loads extra assemblies.");
    sb.AppendLine("- A clean scan means the scanned DLLs do not directly bind to known missing public MonoGame members; it does not prove every runtime path in a mod is supported.");
    sb.AppendLine();

    File.WriteAllText(ReportPath, sb.ToString());
}

void AppendPathList(StringBuilder sb, string title, IReadOnlyList<string> paths)
{
    sb.AppendLine($"### {title}");
    sb.AppendLine();
    if (paths.Count == 0)
    {
        sb.AppendLine("None.");
        sb.AppendLine();
        return;
    }

    foreach (var path in paths)
        sb.AppendLine($"- `{Relative(path)}`");
    sb.AppendLine();
}

void AppendSet(StringBuilder sb, string title, SortedSet<string> items)
{
    sb.AppendLine($"#### {title}");
    sb.AppendLine();
    if (items.Count == 0)
    {
        sb.AppendLine("None.");
        sb.AppendLine();
        return;
    }

    sb.AppendLine("```text");
    foreach (var item in items)
        sb.AppendLine(item);
    sb.AppendLine("```");
    sb.AppendLine();
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

void AddSearchDirectory(ISet<string> directories, string path)
{
    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        directories.Add(Path.GetFullPath(path));
}

void AddResolverDirectory(DefaultAssemblyResolver resolver, string path)
{
    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        resolver.AddSearchDirectory(Path.GetFullPath(path));
}

bool IsAppleSidecar(string path)
{
    return Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal);
}

string FirstExisting(params string[] paths)
{
    foreach (var path in paths)
    {
        if (File.Exists(path))
            return path;
    }

    return paths.First();
}

string Relative(string path)
{
    var fullPath = Path.GetFullPath(path);
    return fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        ? Path.GetRelativePath(Root, fullPath)
        : fullPath;
}

sealed class ScanResult
{
    public string Path { get; set; }
    public string Error { get; set; }
    public SortedSet<string> MonoGameTypeRefs { get; } = new SortedSet<string>(StringComparer.Ordinal);
    public SortedSet<string> MonoGameMemberRefs { get; } = new SortedSet<string>(StringComparer.Ordinal);
    public SortedSet<string> MissingTypeRefs { get; } = new SortedSet<string>(StringComparer.Ordinal);
    public SortedSet<string> MissingMethodRefs { get; } = new SortedSet<string>(StringComparer.Ordinal);
    public SortedSet<string> MissingFieldRefs { get; } = new SortedSet<string>(StringComparer.Ordinal);
    public bool HasMonoGameRefs => MonoGameTypeRefs.Count > 0 || MonoGameMemberRefs.Count > 0;
    public bool HasMissingRefs => MissingTypeRefs.Count > 0 || MissingMethodRefs.Count > 0 || MissingFieldRefs.Count > 0;
}
