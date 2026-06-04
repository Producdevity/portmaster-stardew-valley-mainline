using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string LinuxRuntimeTarget = ".NETCoreApp,Version=v6.0/linux-arm64";
const string RuntimePackName = "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/6.0.32";

if (!TryParseArguments(args, out var gameDir, out var overlayDir, out var smapiBundleDir, out var smapiPatchAssembly, out var modsDir))
{
    Console.Error.WriteLine("Usage: MainlineGameDataPatcher --game-dir <path> --overlay-dir <path> [--smapi-bundle-dir <path>] [--smapi-patch-assembly <path>] [--mods-dir <path>]");
    return 1;
}

gameDir = Path.GetFullPath(gameDir);
overlayDir = Path.GetFullPath(overlayDir);
smapiBundleDir = string.IsNullOrWhiteSpace(smapiBundleDir) ? string.Empty : Path.GetFullPath(smapiBundleDir);
smapiPatchAssembly = string.IsNullOrWhiteSpace(smapiPatchAssembly) ? string.Empty : Path.GetFullPath(smapiPatchAssembly);
modsDir = string.IsNullOrWhiteSpace(modsDir) ? string.Empty : Path.GetFullPath(modsDir);

if (!Directory.Exists(gameDir))
{
    Console.Error.WriteLine($"Game directory not found: {gameDir}");
    return 1;
}

EnsureRequiredFile(gameDir, "Stardew Valley.dll");
EnsureRequiredFile(gameDir, "Stardew Valley.deps.json");
EnsureRequiredFile(gameDir, "Stardew Valley.runtimeconfig.json");

var depsPath = Path.Combine(gameDir, "Stardew Valley.deps.json");
var runtimeConfigPath = Path.Combine(gameDir, "Stardew Valley.runtimeconfig.json");

PruneRuntimePackManagedFiles(depsPath, gameDir);
DeleteConflictingRuntimeFiles(gameDir);
PatchRuntimeConfig(runtimeConfigPath);
PatchDeps(depsPath);
CopyOverlayFiles(overlayDir, gameDir);
PrepareOptionalSmapiRuntime(gameDir, depsPath, smapiBundleDir, smapiPatchAssembly);
AlignGameAssemblyVersionsForSmapi(gameDir);
PatchGameAssemblyMethods(gameDir);
NormalizeManagedAssemblies(gameDir);
NormalizeManagedAssemblies(modsDir, SearchOption.AllDirectories);

Console.WriteLine($"Prepared Stardew Valley mainline runtime in {gameDir}");
return 0;

static bool TryParseArguments(string[] args, out string gameDir, out string overlayDir, out string smapiBundleDir, out string smapiPatchAssembly, out string modsDir)
{
    gameDir = string.Empty;
    overlayDir = string.Empty;
    smapiBundleDir = string.Empty;
    smapiPatchAssembly = string.Empty;
    modsDir = string.Empty;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--game-dir" when i + 1 < args.Length:
                gameDir = args[++i];
                break;
            case "--overlay-dir" when i + 1 < args.Length:
                overlayDir = args[++i];
                break;
            case "--smapi-bundle-dir" when i + 1 < args.Length:
                smapiBundleDir = args[++i];
                break;
            case "--smapi-patch-assembly" when i + 1 < args.Length:
                smapiPatchAssembly = args[++i];
                break;
            case "--mods-dir" when i + 1 < args.Length:
                modsDir = args[++i];
                break;
            default:
                return false;
        }
    }

    return !string.IsNullOrWhiteSpace(gameDir) && !string.IsNullOrWhiteSpace(overlayDir);
}

static void EnsureRequiredFile(string gameDir, string relativePath)
{
    var path = Path.Combine(gameDir, relativePath);
    if (!File.Exists(path))
    {
        throw new FileNotFoundException($"Required game file is missing: {relativePath}", path);
    }
}

static void PruneRuntimePackManagedFiles(string depsPath, string gameDir)
{
    var root = LoadJsonObject(depsPath);
    var runtimeTarget = root["runtimeTarget"]?["name"]?.GetValue<string>();
    if (string.IsNullOrEmpty(runtimeTarget))
    {
        return;
    }

    var runtimePackRuntime = root["targets"]?[runtimeTarget]?[RuntimePackName]?["runtime"]?.AsObject();
    if (runtimePackRuntime is null)
    {
        return;
    }

    foreach (var runtimeFile in runtimePackRuntime)
    {
        var filePath = Path.Combine(gameDir, runtimeFile.Key);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

static void DeleteConflictingRuntimeFiles(string gameDir)
{
    string[] exactFiles =
    {
        "Stardew Valley.exe",
        "SDL2.dll",
        "soft_oal.dll",
        "libSDL2-2.0.so.0",
        "libSDL2-2.0.0.dylib",
        "libopenal.so.1",
        "libopenal.1.dylib",
        "libSkiaSharp.dll",
        "liblwjgl_lz4.dll",
        "steam_api64.dll",
        "steam_api64.cdx",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "createdump.exe",
        "clretwrc.dll",
        "clrjit.dll",
        "dbgshim.dll",
        "mscordaccore.dll",
        "mscordbi.dll",
        "mscorrc.dll",
        "ucrtbase.dll",
    };

    foreach (var fileName in exactFiles)
    {
        var path = Path.Combine(gameDir, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    foreach (var path in Directory.EnumerateFiles(gameDir, "api-ms-win-*.dll"))
    {
        File.Delete(path);
    }

    foreach (var path in Directory.EnumerateFiles(gameDir, "*.dll"))
    {
        if (IsWindowsNativePe(path))
        {
            File.Delete(path);
        }
    }
}

static void PrepareOptionalSmapiRuntime(string gameDir, string baseDepsPath, string smapiBundleDir, string smapiPatchAssembly)
{
    if (string.IsNullOrWhiteSpace(smapiBundleDir))
    {
        return;
    }

    if (!Directory.Exists(smapiBundleDir))
    {
        throw new DirectoryNotFoundException($"SMAPI bundle directory not found: {smapiBundleDir}");
    }

    EnsureRequiredFile(smapiBundleDir, "StardewModdingAPI.dll");
    EnsureRequiredFile(smapiBundleDir, "StardewModdingAPI.runtimeconfig.json");
    if (!Directory.Exists(Path.Combine(smapiBundleDir, "smapi-internal")))
    {
        throw new DirectoryNotFoundException($"SMAPI bundle is missing smapi-internal: {smapiBundleDir}");
    }

    CopyDirectoryContents(smapiBundleDir, gameDir);

    var smapiDepsPath = Path.Combine(gameDir, "StardewModdingAPI.deps.json");
    var smapiRuntimeConfigPath = Path.Combine(gameDir, "StardewModdingAPI.runtimeconfig.json");
    File.Copy(baseDepsPath, smapiDepsPath, overwrite: true);
    PatchRuntimeConfig(smapiRuntimeConfigPath);

    if (!string.IsNullOrWhiteSpace(smapiPatchAssembly))
    {
        if (!File.Exists(smapiPatchAssembly))
        {
            throw new FileNotFoundException("SMAPI patch assembly source is missing.", smapiPatchAssembly);
        }

        var patchTargetPath = Path.Combine(gameDir, "smapi-internal", "StardewPatches.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(patchTargetPath) ?? gameDir);
        File.Copy(smapiPatchAssembly, patchTargetPath, overwrite: true);
    }
}

static void AlignGameAssemblyVersionsForSmapi(string gameDir)
{
    var smapiAssemblyPath = Path.Combine(gameDir, "StardewModdingAPI.dll");
    if (!File.Exists(smapiAssemblyPath))
    {
        return;
    }

    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(gameDir);
    var readerParameters = new ReaderParameters
    {
        InMemory = true,
        ReadSymbols = false,
        AssemblyResolver = resolver,
    };

    using var smapiAssembly = AssemblyDefinition.ReadAssembly(smapiAssemblyPath, readerParameters);
    var expectedVersions = smapiAssembly.MainModule.AssemblyReferences
        .Where(reference => reference.Name is "Stardew Valley" or "StardewValley.GameData")
        .ToDictionary(reference => reference.Name, reference => reference.Version);

    AlignAssemblyVersion(gameDir, "Stardew Valley.dll", expectedVersions);
    AlignAssemblyVersion(gameDir, "StardewValley.GameData.dll", expectedVersions);
}

static void AlignAssemblyVersion(string gameDir, string fileName, IReadOnlyDictionary<string, Version> expectedVersions)
{
    var assemblyPath = Path.Combine(gameDir, fileName);
    if (!File.Exists(assemblyPath))
    {
        return;
    }

    var assemblyName = Path.GetFileNameWithoutExtension(fileName);
    if (!expectedVersions.TryGetValue(assemblyName, out var expectedVersion))
    {
        return;
    }

    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(gameDir);
    var readerParameters = new ReaderParameters
    {
        InMemory = true,
        ReadSymbols = false,
        AssemblyResolver = resolver,
    };

    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
    if (assembly.Name.Version == expectedVersion)
    {
        return;
    }

    var tempPath = assemblyPath + ".version.tmp";
    assembly.Name.Version = expectedVersion;
    assembly.Write(tempPath, new WriterParameters { WriteSymbols = false });
    File.Move(tempPath, assemblyPath, overwrite: true);
    Console.WriteLine($"Aligned {assemblyName} assembly version to {expectedVersion}");
}

static void PatchRuntimeConfig(string runtimeConfigPath)
{
    var root = LoadJsonObject(runtimeConfigPath);
    var runtimeOptions = root["runtimeOptions"]?.AsObject() ?? new JsonObject();
    root["runtimeOptions"] = runtimeOptions;

    var includedFrameworks = runtimeOptions["includedFrameworks"] as JsonArray;
    runtimeOptions.Remove("includedFrameworks");

    var framework = runtimeOptions["framework"] as JsonObject;
    if (framework is null)
    {
        var selected = includedFrameworks?.Count > 0 ? includedFrameworks[0] as JsonObject : null;
        framework = new JsonObject
        {
            ["name"] = selected?["name"]?.GetValue<string>() ?? "Microsoft.NETCore.App",
            ["version"] = selected?["version"]?.GetValue<string>() ?? "6.0.32",
        };
        runtimeOptions["framework"] = framework;
    }

    WriteJsonObject(runtimeConfigPath, root);
}

static void PatchDeps(string depsPath)
{
    var root = LoadJsonObject(depsPath);
    var runtimeTarget = root["runtimeTarget"]?.AsObject() ?? throw new InvalidOperationException("deps.json is missing runtimeTarget");
    var oldTarget = runtimeTarget["name"]?.GetValue<string>() ?? LinuxRuntimeTarget;
    runtimeTarget["name"] = LinuxRuntimeTarget;

    var targets = root["targets"]?.AsObject() ?? throw new InvalidOperationException("deps.json is missing targets");
    if (targets[oldTarget] is JsonNode oldTargetNode && targets[LinuxRuntimeTarget] is null)
    {
        targets[LinuxRuntimeTarget] = JsonNode.Parse(oldTargetNode.ToJsonString());
    }

    foreach (var targetEntry in targets.ToList())
    {
        if (targetEntry.Value is not JsonObject targetObject)
        {
            continue;
        }

        targetObject.Remove(RuntimePackName);
        foreach (var libraryEntry in targetObject.ToList())
        {
            if (libraryEntry.Value is not JsonObject libraryObject)
            {
                continue;
            }

            if (libraryObject["dependencies"] is not JsonObject dependencies)
            {
                continue;
            }

            foreach (var dependencyName in dependencies.Select(entry => entry.Key).Where(name => name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.win-x64", StringComparison.Ordinal)).ToList())
            {
                dependencies.Remove(dependencyName);
            }
        }
    }

    var libraries = root["libraries"]?.AsObject();
    if (libraries is not null)
    {
        foreach (var libraryName in libraries.Select(entry => entry.Key).Where(name => name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.win-x64", StringComparison.Ordinal)).ToList())
        {
            libraries.Remove(libraryName);
        }
    }

    WriteJsonObject(depsPath, root);
}

static void NormalizeManagedAssemblies(string rootDir, SearchOption searchOption = SearchOption.TopDirectoryOnly)
{
    if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
    {
        return;
    }

    foreach (var filePath in Directory.EnumerateFiles(rootDir, "*", searchOption).Where(path =>
                 path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
    {
        if (Path.GetFileName(filePath).Equals("MonoGame.Framework.dll", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!NeedsArchitectureNormalization(filePath))
        {
            continue;
        }

        NormalizeAssemblyArchitecture(filePath);
    }
}

static void PatchGameAssemblyMethods(string gameDir)
{
    var assemblyPath = Path.Combine(gameDir, "Stardew Valley.dll");
    var tempPath = assemblyPath + ".methods.tmp";
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(gameDir);

    var readerParameters = new ReaderParameters
    {
        InMemory = true,
        ReadSymbols = false,
        AssemblyResolver = resolver,
    };

    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
    var module = assembly.MainModule;

    var optionsType = GetRequiredType(module, "StardewValley.Options");
    var game1Type = GetRequiredType(module, "StardewValley.Game1");
    var programType = GetRequiredType(module, "StardewValley.Program");
    var localizedContentManagerType = GetRequiredType(module, "StardewValley.LocalizedContentManager");
    var titleMenuType = GetRequiredType(module, "StardewValley.Menus.TitleMenu");
    var audioEngineWrapperType = GetRequiredType(module, "StardewValley.Audio.AudioEngineWrapper");
    var dummyAudioCategoryType = GetRequiredType(module, "StardewValley.Audio.DummyAudioCategory");
    var inputStateType = GetRequiredType(module, "StardewValley.InputState");
    var toggleFullscreen = GetRequiredMethod(game1Type, "toggleFullscreen", 0);
    var toggleNonBorderlessWindowedFullscreen = GetRequiredMethod(game1Type, "toggleNonBorderlessWindowedFullscreen", 0);
    var inputStateUpdateStates = GetRequiredMethod(inputStateType, "UpdateStates", 0);
    var inputStateSetMousePosition = GetRequiredMethod(inputStateType, "SetMousePosition", 2);
    var optionsUiScaleGetter = GetRequiredMethod(optionsType, "get_uiScale", 0);
    var optionsZoomLevelGetter = GetRequiredMethod(optionsType, "get_zoomLevel", 0);

    var game1SingletonField = GetRequiredField(game1Type, "game1");
    var game1ContentField = GetRequiredField(game1Type, "content");
    var game1UiViewportField = GetRequiredField(game1Type, "uiViewport");
    var game1ViewportField = GetRequiredField(game1Type, "viewport");
    var game1ZoomModifierField = GetRequiredField(game1Type, "zoomModifier");
    var game1TakingMapScreenshotField = GetRequiredField(game1Type, "takingMapScreenshot");
    var optionsBaseUiScaleField = GetRequiredField(optionsType, "baseUIScale");
    var optionsBaseZoomLevelField = GetRequiredField(optionsType, "baseZoomLevel");
    var inputStateSimulatedMousePositionField = GetRequiredField(inputStateType, "_simulatedMousePosition");
    var inputStateCurrentKeyboardStateField = GetRequiredField(inputStateType, "_currentKeyboardState");
    var inputStateCurrentMouseStateField = GetRequiredField(inputStateType, "_currentMouseState");
    var inputStateCurrentGamepadStateField = GetRequiredField(inputStateType, "_currentGamepadState");
    var optionsFullscreenField = GetRequiredField(optionsType, "fullscreen");
    var optionsWindowedBorderlessFullscreenField = GetRequiredField(optionsType, "windowedBorderlessFullscreen");

    var game1GetOptions = GetRequiredMethod(game1Type, "get_options", 0);
    var getWindow = GetRequiredReferencedMethodFromAny(new[] { toggleFullscreen, optionsUiScaleGetter, optionsZoomLevelGetter }, "StardewValley.InstanceGame", "get_Window");
    var getClientBounds = GetRequiredReferencedMethodFromAny(new[] { toggleFullscreen, optionsUiScaleGetter, optionsZoomLevelGetter }, "Microsoft.Xna.Framework.GameWindow", "get_ClientBounds");
    var rectangleWidthField = GetRequiredReferencedFieldFromAny(new[] { toggleFullscreen, optionsUiScaleGetter, optionsZoomLevelGetter }, "Microsoft.Xna.Framework.Rectangle", "Width");
    var rectangleHeightField = GetRequiredReferencedFieldFromAny(new[] { toggleFullscreen, optionsUiScaleGetter, optionsZoomLevelGetter }, "Microsoft.Xna.Framework.Rectangle", "Height");
    var pointXField = GetRequiredReferencedField(inputStateSetMousePosition, "Microsoft.Xna.Framework.Point", "X");
    var pointYField = GetRequiredReferencedField(inputStateSetMousePosition, "Microsoft.Xna.Framework.Point", "Y");
    var keyboardGetState = GetRequiredReferencedMethod(inputStateUpdateStates, "Microsoft.Xna.Framework.Input.Keyboard", "GetState");
    var gamePadGetState = GetRequiredReferencedMethod(inputStateUpdateStates, "Microsoft.Xna.Framework.Input.GamePad", "GetState");
    var game1GetPlayerOneIndex = GetRequiredReferencedMethod(inputStateUpdateStates, "StardewValley.Game1", "get_playerOneIndex");
    var instanceGameIsMainInstance = GetRequiredReferencedMethod(inputStateSetMousePosition, "StardewValley.InstanceGame", "get_IsMainInstance");
    var mouseStateScrollWheelValue = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_ScrollWheelValue");
    var mouseStateLeftButton = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_LeftButton");
    var mouseStateMiddleButton = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_MiddleButton");
    var mouseStateRightButton = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_RightButton");
    var mouseStateXButton1 = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_XButton1");
    var mouseStateXButton2 = GetRequiredReferencedMethod(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", "get_XButton2");
    var mouseStateCtor = GetRequiredReferencedConstructor(inputStateSetMousePosition, "Microsoft.Xna.Framework.Input.MouseState", 8);
    var getRootDirectory = GetRequiredReferencedMethod(
        GetRequiredMethod(localizedContentManagerType, "GetContentRoot", 0),
        "Microsoft.Xna.Framework.Content.ContentManager",
        "get_RootDirectory");
    var mathFMin = module.ImportReference(
        typeof(MathF).GetMethod(nameof(MathF.Min), new[] { typeof(float), typeof(float) })
        ?? throw new InvalidOperationException("Unable to import MathF.Min"));

    RewriteUiScaleGetter(
        optionsUiScaleGetter,
        game1SingletonField,
        game1ZoomModifierField,
        optionsBaseUiScaleField,
        getWindow,
        getClientBounds,
        rectangleWidthField,
        rectangleHeightField,
        mathFMin);

    RewriteZoomLevelGetter(
        optionsZoomLevelGetter,
        game1SingletonField,
        game1ZoomModifierField,
        game1TakingMapScreenshotField,
        optionsBaseZoomLevelField,
        getWindow,
        getClientBounds,
        rectangleWidthField,
        rectangleHeightField,
        mathFMin);

    RewriteContentRootGetter(
        GetRequiredMethod(localizedContentManagerType, "GetContentRoot", 0),
        game1ContentField,
        getRootDirectory);

    RewriteAudioEngineWrapperGetCategory(
        GetRequiredMethod(audioEngineWrapperType, "GetCategory", 1),
        module.ImportReference(
            dummyAudioCategoryType.Methods.FirstOrDefault(m => m.Name == ".ctor" && !m.IsStatic && m.Parameters.Count == 0)
            ?? throw new InvalidOperationException("Unable to find StardewValley.Audio.DummyAudioCategory::.ctor()")));

    RewriteTitleMenuViewportUsage(
        GetRequiredMethod(titleMenuType, "gameWindowSizeChanged", 2),
        game1ViewportField,
        game1UiViewportField,
        1);

    RewriteTitleMenuViewportUsage(
        GetRequiredMethod(titleMenuType, "update", 1),
        game1ViewportField,
        game1UiViewportField,
        2);

    RewriteInputStateUpdateStates(
        inputStateUpdateStates,
        inputStateCurrentKeyboardStateField,
        inputStateCurrentGamepadStateField,
        keyboardGetState,
        game1GetPlayerOneIndex,
        gamePadGetState);

    RewriteInputStateSetMousePosition(
        inputStateSetMousePosition,
        game1SingletonField,
        inputStateSimulatedMousePositionField,
        inputStateCurrentMouseStateField,
        pointXField,
        pointYField,
        instanceGameIsMainInstance,
        mouseStateScrollWheelValue,
        mouseStateLeftButton,
        mouseStateMiddleButton,
        mouseStateRightButton,
        mouseStateXButton1,
        mouseStateXButton2,
        mouseStateCtor);

    RewriteBooleanReturn(GetRequiredMethod(optionsType, "isCurrentlyWindowedBorderless", 0), true);
    RewriteBooleanReturn(GetRequiredMethod(optionsType, "isCurrentlyFullscreen", 0), false);
    RewriteBooleanReturn(GetRequiredMethod(optionsType, "isCurrentlyWindowed", 0), false);
    RewriteWindowModeOptionSetter(GetRequiredMethodByParameterType(optionsType, "setWindowedOption", "System.String"), optionsFullscreenField, optionsWindowedBorderlessFullscreenField);
    RewriteWindowModeOptionSetter(GetRequiredMethodByParameterType(optionsType, "setWindowedOption", "System.Int32"), optionsFullscreenField, optionsWindowedBorderlessFullscreenField);
    RewriteGameWindowModeToggle(toggleFullscreen, game1GetOptions, optionsFullscreenField, optionsWindowedBorderlessFullscreenField);
    RewriteGameWindowModeToggle(toggleNonBorderlessWindowedFullscreen, game1GetOptions, optionsFullscreenField, optionsWindowedBorderlessFullscreenField);
    PatchOutDisplayModeChanges(game1Type, optionsType);

    assembly.Write(tempPath, new WriterParameters { WriteSymbols = false });
    File.Move(tempPath, assemblyPath, overwrite: true);
}

static void RewriteUiScaleGetter(
    MethodDefinition method,
    FieldReference game1SingletonField,
    FieldReference zoomModifierField,
    FieldReference baseUiScaleField,
    MethodReference getWindow,
    MethodReference getClientBounds,
    FieldReference rectangleWidthField,
    FieldReference rectangleHeightField,
    MethodReference mathFMin)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();

    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldfld, baseUiScaleField));
    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Ldfld, zoomModifierField));
    il.Append(il.Create(OpCodes.Mul));
    EmitScreenRatio(il, game1SingletonField, getWindow, getClientBounds, rectangleWidthField, rectangleHeightField, mathFMin);
    il.Append(il.Create(OpCodes.Mul));
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteZoomLevelGetter(
    MethodDefinition method,
    FieldReference game1SingletonField,
    FieldReference zoomModifierField,
    FieldReference takingMapScreenshotField,
    FieldReference baseZoomLevelField,
    MethodReference getWindow,
    MethodReference getClientBounds,
    FieldReference rectangleWidthField,
    FieldReference rectangleHeightField,
    MethodReference mathFMin)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();
    var nonScreenshot = il.Create(OpCodes.Nop);

    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Ldfld, takingMapScreenshotField));
    il.Append(il.Create(OpCodes.Brfalse_S, nonScreenshot));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldfld, baseZoomLevelField));
    EmitScreenRatio(il, game1SingletonField, getWindow, getClientBounds, rectangleWidthField, rectangleHeightField, mathFMin);
    il.Append(il.Create(OpCodes.Mul));
    il.Append(il.Create(OpCodes.Ret));

    il.Append(nonScreenshot);
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldfld, baseZoomLevelField));
    EmitScreenRatio(il, game1SingletonField, getWindow, getClientBounds, rectangleWidthField, rectangleHeightField, mathFMin);
    il.Append(il.Create(OpCodes.Mul));
    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Ldfld, zoomModifierField));
    il.Append(il.Create(OpCodes.Mul));
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteContentRootGetter(
    MethodDefinition method,
    FieldReference game1ContentField,
    MethodReference getRootDirectory)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();

    il.Append(il.Create(OpCodes.Ldsfld, game1ContentField));
    il.Append(il.Create(OpCodes.Callvirt, getRootDirectory));
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteAudioEngineWrapperGetCategory(
    MethodDefinition method,
    MethodReference dummyAudioCategoryCtor)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();

    il.Append(il.Create(OpCodes.Newobj, dummyAudioCategoryCtor));
    il.Append(il.Create(OpCodes.Ret));
}

static void PatchOutDisplayModeChanges(params TypeDefinition[] types)
{
    foreach (var method in types.SelectMany(type => type.Methods).Where(method => method.HasBody))
    {
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Callvirt ||
                instruction.Operand is not MethodReference methodReference ||
                methodReference.DeclaringType.FullName != "Microsoft.Xna.Framework.GraphicsDeviceManager" ||
                methodReference.Parameters.Count != 0 ||
                methodReference.ReturnType.FullName != "System.Void")
            {
                continue;
            }

            if (methodReference.Name is not ("ApplyChanges" or "ToggleFullScreen"))
            {
                continue;
            }

            instruction.OpCode = OpCodes.Pop;
            instruction.Operand = null;
        }
    }
}

static void RewriteBooleanReturn(MethodDefinition method, bool value)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();
    il.Append(il.Create(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteWindowModeOptionSetter(
    MethodDefinition method,
    FieldReference fullscreenField,
    FieldReference windowedBorderlessFullscreenField)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();
    EmitSetWindowModeFields(il, fullscreenField, windowedBorderlessFullscreenField);
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteGameWindowModeToggle(
    MethodDefinition method,
    MethodReference getOptions,
    FieldReference fullscreenField,
    FieldReference windowedBorderlessFullscreenField)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();

    il.Append(il.Create(OpCodes.Call, getOptions));
    il.Append(il.Create(OpCodes.Ldc_I4_0));
    il.Append(il.Create(OpCodes.Stfld, fullscreenField));
    il.Append(il.Create(OpCodes.Call, getOptions));
    il.Append(il.Create(OpCodes.Ldc_I4_1));
    il.Append(il.Create(OpCodes.Stfld, windowedBorderlessFullscreenField));
    il.Append(il.Create(OpCodes.Ret));
}

static void EmitSetWindowModeFields(
    ILProcessor il,
    FieldReference fullscreenField,
    FieldReference windowedBorderlessFullscreenField)
{
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldc_I4_0));
    il.Append(il.Create(OpCodes.Stfld, fullscreenField));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldc_I4_1));
    il.Append(il.Create(OpCodes.Stfld, windowedBorderlessFullscreenField));
}

static void RewriteTitleMenuViewportUsage(
    MethodDefinition method,
    FieldReference viewportField,
    FieldReference uiViewportField,
    int expectedReplacements)
{
    var originalViewportRefs = 0;
    var existingUiViewportRefs = 0;

    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.Operand is not FieldReference field)
        {
            continue;
        }

        if (field.FullName == viewportField.FullName)
        {
            originalViewportRefs++;
        }
        else if (field.FullName == uiViewportField.FullName)
        {
            existingUiViewportRefs++;
        }
    }

    var replacements = 0;
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.Operand is not FieldReference field)
        {
            continue;
        }

        if (field.FullName != viewportField.FullName)
        {
            continue;
        }

        instruction.Operand = uiViewportField;
        replacements++;
    }

    if (replacements == expectedReplacements)
    {
        return;
    }

    if (replacements == 0 && originalViewportRefs == 0 && existingUiViewportRefs >= expectedReplacements)
    {
        return;
    }

    if (replacements != expectedReplacements)
    {
        throw new InvalidOperationException(
            $"Unexpected TitleMenu viewport patch count for {method.FullName}: expected {expectedReplacements}, got {replacements} (original viewport refs={originalViewportRefs}, existing uiViewport refs={existingUiViewportRefs})");
    }
}

static void RewriteInputStateUpdateStates(
    MethodDefinition method,
    FieldReference currentKeyboardStateField,
    FieldReference currentGamepadStateField,
    MethodReference keyboardGetState,
    MethodReference game1GetPlayerOneIndex,
    MethodReference gamePadGetState)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();
    var noGamepad = il.Create(OpCodes.Nop);

    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Call, keyboardGetState));
    il.Append(il.Create(OpCodes.Stfld, currentKeyboardStateField));

    il.Append(il.Create(OpCodes.Call, game1GetPlayerOneIndex));
    il.Append(il.Create(OpCodes.Ldc_I4_0));
    il.Append(il.Create(OpCodes.Blt_S, noGamepad));

    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Call, game1GetPlayerOneIndex));
    il.Append(il.Create(OpCodes.Call, gamePadGetState));
    il.Append(il.Create(OpCodes.Stfld, currentGamepadStateField));
    il.Append(il.Create(OpCodes.Ret));

    il.Append(noGamepad);
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldflda, currentGamepadStateField));
    il.Append(il.Create(OpCodes.Initobj, currentGamepadStateField.FieldType));
    il.Append(il.Create(OpCodes.Ret));
}

static void RewriteInputStateSetMousePosition(
    MethodDefinition method,
    FieldReference game1SingletonField,
    FieldReference simulatedMousePositionField,
    FieldReference currentMouseStateField,
    FieldReference pointXField,
    FieldReference pointYField,
    MethodReference instanceGameIsMainInstance,
    MethodReference mouseStateScrollWheelValue,
    MethodReference mouseStateLeftButton,
    MethodReference mouseStateMiddleButton,
    MethodReference mouseStateRightButton,
    MethodReference mouseStateXButton1,
    MethodReference mouseStateXButton2,
    MethodReference mouseStateCtor)
{
    ResetMethodBody(method);
    var il = method.Body.GetILProcessor();
    var mainInstance = il.Create(OpCodes.Nop);

    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Callvirt, instanceGameIsMainInstance));
    il.Append(il.Create(OpCodes.Brtrue_S, mainInstance));

    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldflda, simulatedMousePositionField));
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Stfld, pointXField));
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldflda, simulatedMousePositionField));
    il.Append(il.Create(OpCodes.Ldarg_2));
    il.Append(il.Create(OpCodes.Stfld, pointYField));
    il.Append(il.Create(OpCodes.Ret));

    il.Append(mainInstance);
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldarg_1));
    il.Append(il.Create(OpCodes.Ldarg_2));
    EmitMouseStateValue(il, currentMouseStateField, mouseStateScrollWheelValue);
    EmitMouseStateValue(il, currentMouseStateField, mouseStateLeftButton);
    EmitMouseStateValue(il, currentMouseStateField, mouseStateMiddleButton);
    EmitMouseStateValue(il, currentMouseStateField, mouseStateRightButton);
    EmitMouseStateValue(il, currentMouseStateField, mouseStateXButton1);
    EmitMouseStateValue(il, currentMouseStateField, mouseStateXButton2);
    il.Append(il.Create(OpCodes.Newobj, mouseStateCtor));
    il.Append(il.Create(OpCodes.Stfld, currentMouseStateField));
    il.Append(il.Create(OpCodes.Ret));
}

static void EmitMouseStateValue(ILProcessor il, FieldReference currentMouseStateField, MethodReference getter)
{
    il.Append(il.Create(OpCodes.Ldarg_0));
    il.Append(il.Create(OpCodes.Ldflda, currentMouseStateField));
    il.Append(il.Create(OpCodes.Call, getter));
}

static void EmitScreenRatio(
    ILProcessor il,
    FieldReference game1SingletonField,
    MethodReference getWindow,
    MethodReference getClientBounds,
    FieldReference rectangleWidthField,
    FieldReference rectangleHeightField,
    MethodReference mathFMin)
{
    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Callvirt, getWindow));
    il.Append(il.Create(OpCodes.Callvirt, getClientBounds));
    il.Append(il.Create(OpCodes.Ldfld, rectangleHeightField));
    il.Append(il.Create(OpCodes.Conv_R4));
    il.Append(il.Create(OpCodes.Ldc_R4, 768f));
    il.Append(il.Create(OpCodes.Div));

    il.Append(il.Create(OpCodes.Ldsfld, game1SingletonField));
    il.Append(il.Create(OpCodes.Callvirt, getWindow));
    il.Append(il.Create(OpCodes.Callvirt, getClientBounds));
    il.Append(il.Create(OpCodes.Ldfld, rectangleWidthField));
    il.Append(il.Create(OpCodes.Conv_R4));
    il.Append(il.Create(OpCodes.Ldc_R4, 1366f));
    il.Append(il.Create(OpCodes.Div));

    il.Append(il.Create(OpCodes.Call, mathFMin));
}

static void ResetMethodBody(MethodDefinition method)
{
    method.Body.ExceptionHandlers.Clear();
    method.Body.Variables.Clear();
    method.Body.InitLocals = false;
    method.Body.Instructions.Clear();
}

static TypeDefinition GetRequiredType(ModuleDefinition module, string fullName)
{
    return module.Types.FirstOrDefault(type => type.FullName == fullName)
           ?? throw new InvalidOperationException($"Unable to find type: {fullName}");
}

static MethodDefinition GetRequiredMethod(TypeDefinition type, string name, int parameterCount)
{
    return type.Methods.FirstOrDefault(method => method.Name == name && method.Parameters.Count == parameterCount)
           ?? throw new InvalidOperationException($"Unable to find method {type.FullName}::{name}({parameterCount})");
}

static MethodDefinition GetRequiredMethodByParameterType(TypeDefinition type, string name, string parameterTypeFullName)
{
    return type.Methods.FirstOrDefault(method =>
               method.Name == name &&
               method.Parameters.Count == 1 &&
               method.Parameters[0].ParameterType.FullName == parameterTypeFullName)
           ?? throw new InvalidOperationException($"Unable to find method {type.FullName}::{name}({parameterTypeFullName})");
}

static FieldDefinition GetRequiredField(TypeDefinition type, string name)
{
    return type.Fields.FirstOrDefault(field => field.Name == name)
           ?? throw new InvalidOperationException($"Unable to find field {type.FullName}::{name}");
}

static MethodReference GetRequiredReferencedMethod(MethodDefinition method, string declaringTypeFullName, string name)
{
    return method.Body.Instructions
               .Where(ins => ins.OpCode.FlowControl == FlowControl.Call && ins.Operand is MethodReference)
               .Select(ins => (MethodReference)ins.Operand)
               .FirstOrDefault(reference => reference.Name == name && reference.DeclaringType.FullName == declaringTypeFullName)
           ?? throw new InvalidOperationException($"Unable to find referenced method: {declaringTypeFullName}::{name}");
}

static MethodReference GetRequiredReferencedMethodFromAny(IEnumerable<MethodDefinition> methods, string declaringTypeFullName, string name)
{
    foreach (var method in methods)
    {
        var reference = method.Body.Instructions
            .Where(ins => ins.OpCode.FlowControl == FlowControl.Call && ins.Operand is MethodReference)
            .Select(ins => (MethodReference)ins.Operand)
            .FirstOrDefault(reference => reference.Name == name && reference.DeclaringType.FullName == declaringTypeFullName);

        if (reference is not null)
        {
            return reference;
        }
    }

    throw new InvalidOperationException($"Unable to find referenced method: {declaringTypeFullName}::{name}");
}

static MethodReference GetRequiredReferencedConstructor(MethodDefinition method, string declaringTypeFullName, int parameterCount)
{
    return method.Body.Instructions
               .Where(ins => ins.OpCode == OpCodes.Newobj && ins.Operand is MethodReference)
               .Select(ins => (MethodReference)ins.Operand)
               .FirstOrDefault(reference =>
                   reference.Name == ".ctor" &&
                   reference.DeclaringType.FullName == declaringTypeFullName &&
                   reference.Parameters.Count == parameterCount)
           ?? throw new InvalidOperationException($"Unable to find referenced constructor: {declaringTypeFullName}({parameterCount})");
}

static FieldReference GetRequiredReferencedField(MethodDefinition method, string declaringTypeFullName, string name)
{
    return method.Body.Instructions
               .Where(ins => ins.Operand is FieldReference)
               .Select(ins => (FieldReference)ins.Operand)
               .FirstOrDefault(reference => reference.Name == name && reference.DeclaringType.FullName == declaringTypeFullName)
           ?? throw new InvalidOperationException($"Unable to find referenced field: {declaringTypeFullName}::{name}");
}

static FieldReference GetRequiredReferencedFieldFromAny(IEnumerable<MethodDefinition> methods, string declaringTypeFullName, string name)
{
    foreach (var method in methods)
    {
        var reference = method.Body.Instructions
            .Where(ins => ins.Operand is FieldReference)
            .Select(ins => (FieldReference)ins.Operand)
            .FirstOrDefault(reference => reference.Name == name && reference.DeclaringType.FullName == declaringTypeFullName);

        if (reference is not null)
        {
            return reference;
        }
    }

    throw new InvalidOperationException($"Unable to find referenced field: {declaringTypeFullName}::{name}");
}

static bool NeedsArchitectureNormalization(string assemblyPath)
{
    var bytes = File.ReadAllBytes(assemblyPath);
    if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
    {
        return false;
    }

    var peOffset = BitConverter.ToInt32(bytes, 0x3C);
    if (peOffset <= 0 || peOffset + 160 >= bytes.Length)
    {
        return false;
    }

    var machine = BitConverter.ToUInt16(bytes, peOffset + 4);
    var optionalMagic = BitConverter.ToUInt16(bytes, peOffset + 24);
    var dataDirectoryOffset = peOffset + 24 + (optionalMagic == 0x20B ? 112 : 96);
    if (dataDirectoryOffset + 14 * 8 + 4 > bytes.Length)
    {
        return false;
    }

    var cliHeaderRva = BitConverter.ToUInt32(bytes, dataDirectoryOffset + 14 * 8);
    return machine == 0x8664 && cliHeaderRva != 0;
}

static bool IsWindowsNativePe(string assemblyPath)
{
    var bytes = File.ReadAllBytes(assemblyPath);
    if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z')
    {
        return false;
    }

    var peOffset = BitConverter.ToInt32(bytes, 0x3C);
    if (peOffset <= 0 || peOffset + 160 >= bytes.Length)
    {
        return false;
    }

    var optionalMagic = BitConverter.ToUInt16(bytes, peOffset + 24);
    var dataDirectoryOffset = peOffset + 24 + (optionalMagic == 0x20B ? 112 : 96);
    if (dataDirectoryOffset + 14 * 8 + 4 > bytes.Length)
    {
        return false;
    }

    var cliHeaderRva = BitConverter.ToUInt32(bytes, dataDirectoryOffset + 14 * 8);
    return cliHeaderRva == 0;
}

static void NormalizeAssemblyArchitecture(string assemblyPath)
{
    var tempPath = assemblyPath + ".tmp";
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory);

    var readerParameters = new ReaderParameters
    {
        InMemory = true,
        ReadSymbols = false,
        AssemblyResolver = resolver,
    };

    using var module = ModuleDefinition.ReadModule(assemblyPath, readerParameters);
    module.Architecture = TargetArchitecture.I386;
    module.Attributes |= ModuleAttributes.ILOnly;
    module.Attributes &= ~ModuleAttributes.Required32Bit;
    module.Attributes &= ~ModuleAttributes.Preferred32Bit;
    module.Write(tempPath, new WriterParameters { WriteSymbols = false });
    File.Move(tempPath, assemblyPath, overwrite: true);
}

static void CopyOverlayFiles(string overlayDir, string gameDir)
{
    if (!Directory.Exists(overlayDir))
    {
        return;
    }

    foreach (var sourcePath in Directory.EnumerateFiles(overlayDir, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(overlayDir, sourcePath);
        var destinationPath = Path.Combine(gameDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? gameDir);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}

static void CopyDirectoryContents(string sourceDir, string destinationDir)
{
    foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDir, sourcePath);
        var destinationPath = Path.Combine(destinationDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationDir);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}

static JsonObject LoadJsonObject(string path)
{
    return JsonNode.Parse(
               File.ReadAllText(path),
               documentOptions: new JsonDocumentOptions
               {
                   AllowTrailingCommas = true,
                   CommentHandling = JsonCommentHandling.Skip,
               })?.AsObject()
           ?? throw new InvalidOperationException($"Unable to parse JSON from {path}");
}

static void WriteJsonObject(string path, JsonObject root)
{
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
}
