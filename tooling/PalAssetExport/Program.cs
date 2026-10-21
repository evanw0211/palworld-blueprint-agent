using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

// PalAssetExport - small CLI over CUE4Parse for pulling Palworld build-object meshes
// out of Pal-Windows.pak.
//
// NOTE: DefaultFileProvider.Initialize() only *registers* a VFS. Mount() is what
// actually reads the pak index -- omitting it leaves Files empty (0 of 185141).

// Default install path; the PALWORLD_PAKS environment variable overrides it.
const string DefaultPakDir = @"D:\SteamLibrary\steamapps\common\Palworld\Pal\Content\Paks";
var PakDir = Environment.GetEnvironmentVariable("PALWORLD_PAKS") is { Length: > 0 } envPakDir
    ? envPakDir
    : DefaultPakDir;

// Exit-code contract (documented in README): 0 = success, 1 = the command ran but some
// input/export step failed, 2 = usage error (missing/unknown argument, a numeric option that
// does not parse, outDir outside the repository root). This runs before every command,
// including the reflection diagnostics, so a typo costs nothing and never mounts the pak.
if (args.Length == 0)
{
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

var minArgs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["list"] = 2, ["info"] = 1, ["inspect"] = 2, ["dumptable"] = 3,
    ["exporticons"] = 3, ["exporttex"] = 3, ["exportmesh"] = 3,
    ["exportbps"] = 3, ["exportall"] = 3,
    ["findapi"] = 2, ["probe"] = 1, ["mapapi"] = 1, ["convapi"] = 1,
    ["whereis"] = 2, ["dump"] = 2,
};
if (!minArgs.TryGetValue(args[0], out var minArgCount))
{
    Console.Error.WriteLine($"ERROR: unknown command '{args[0]}'");
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}
if (args.Length < minArgCount)
{
    Console.Error.WriteLine($"ERROR: '{args[0]}' needs at least {minArgCount} argument(s) including the command name, got {args.Length}");
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

// Numeric options that used to throw FormatException from int.Parse deep inside a case.
if (args[0] == "list")
{
    for (int i = 2; i < args.Length - 1; i++)
        if (args[i] == "--limit" && !int.TryParse(args[i + 1], out _))
        {
            Console.Error.WriteLine($"ERROR: --limit expects an integer, got '{args[i + 1]}'");
            PrintUsage();
            Environment.ExitCode = 2;
            return;
        }
}
if (args[0] == "exporticons" && args.Length > 3 && !int.TryParse(args[3], out _))
{
    Console.Error.WriteLine($"ERROR: exporticons [limit] expects an integer, got '{args[3]}'");
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

// `findapi <Name>` locates an extension method without guessing namespaces.
if (args.Length > 0 && args[0] == "findapi")
{
    var sought = args[1];
    var asm = typeof(CUE4Parse.FileProvider.DefaultFileProvider).Assembly;
    foreach (var t in asm.GetTypes())
    {
        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!string.Equals(m.Name, sought, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"  namespace {t.Namespace} | static class {t.Name}");
            Console.WriteLine($"    {m.ReturnType.Name} {m.Name}(" +
                string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        }
    }
    // also list the namespaces of any type mentioning the word, to spot siblings
    return;
}

// `probe` dumps the loading API surface in one shot instead of guessing.
if (args.Length > 0 && args[0] == "probe")
{
    var asm = typeof(CUE4Parse.FileProvider.DefaultFileProvider).Assembly;
    Console.WriteLine("assemblies loaded:");
    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        Console.WriteLine($"  {a.GetName().Name} {a.GetName().Version}");

    Console.WriteLine("\n== every public method whose name contains 'Load' ==");
    foreach (var t in asm.GetTypes().Where(t => t.IsPublic))
    {
        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
        {
            if (!m.Name.Contains("Load", StringComparison.OrdinalIgnoreCase)) continue;
            var kind = m.IsStatic ? "static" : "inst  ";
            Console.WriteLine($"  {kind} {t.Name}.{m.Name}(" +
                string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ") -> " + m.ReturnType.Name);
        }
    }

    Console.WriteLine("\n== IPackage members ==");
    var ipkg = asm.GetTypes().FirstOrDefault(t => t.Name == "IPackage");
    if (ipkg != null)
        foreach (var m in ipkg.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            Console.WriteLine("  " + m.MemberType + " " + m.Name);
    else
        Console.WriteLine("  IPackage type not found");

    Console.WriteLine("\n== public types with 'Package' or 'Provider' in the name ==");
    foreach (var t in asm.GetTypes().Where(t => t.IsPublic && (t.Name.Contains("Package") || t.Name.Contains("Provider"))).OrderBy(t => t.FullName))
        Console.WriteLine("  " + t.FullName);
    return;
}

// `mapapi` finds where the usmap mappings provider is supposed to be attached.
if (args.Length > 0 && args[0] == "mapapi")
{
    var asm = typeof(CUE4Parse.FileProvider.DefaultFileProvider).Assembly;
    foreach (var tn in new[] { "VersionContainer", "AbstractFileProvider", "IFileProvider", "DefaultFileProvider", "MappingsContainer", "UsmapTypeMappingsProvider", "FileUsmapTypeMappingsProvider", "AbstractTypeMappingsProvider" })
    {
        var t = asm.GetTypes().FirstOrDefault(x => x.Name == tn);
        Console.WriteLine($"== {tn} ==");
        if (t == null) { Console.WriteLine("  NOT FOUND"); continue; }
        Console.WriteLine($"  abstract={t.IsAbstract} base={t.BaseType?.Name}");
        foreach (var c in t.GetConstructors())
            Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Console.WriteLine($"  prop {p.PropertyType.Name} {p.Name} {{ {(p.CanWrite ? "get;set" : "get")} }}");
        foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Console.WriteLine($"  field {f.FieldType.Name} {f.Name}");
    }
    return;
}

// `convapi` probes the CUE4Parse-Conversion export surface.
if (args.Length > 0 && args[0] == "convapi")
{
    var dll = Path.Combine(AppContext.BaseDirectory, "CUE4Parse-Conversion.dll");
    Console.WriteLine("conversion dll: " + dll + "  exists=" + File.Exists(dll));
    if (!File.Exists(dll))
    {
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, "*.dll").OrderBy(x => x))
            Console.WriteLine("   present: " + Path.GetFileName(f));
        return;
    }
    var conv = System.Reflection.Assembly.LoadFrom(dll);
    Console.WriteLine("conversion assembly: " + conv.GetName().Name + " " + conv.GetName().Version + "\n");
    foreach (var t in conv.GetTypes().Where(t => t.IsPublic).OrderBy(t => t.FullName))
    {
        if (!(t.Name.Contains("Export") || t.Name.Contains("Exporter") || t.Name.Contains("Mesh"))) continue;
        Console.WriteLine($"== {t.FullName}  (abstract={t.IsAbstract}, enum={t.IsEnum})");
        if (t.IsEnum)
        {
            foreach (var n in Enum.GetNames(t)) Console.WriteLine("    " + n);
            continue;
        }
        foreach (var c in t.GetConstructors())
            Console.WriteLine("    ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            Console.WriteLine($"    {m.ReturnType.Name} {m.Name}(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Console.WriteLine($"    prop {p.PropertyType.Name} {p.Name}");
    }
    return;
}

// `whereis <TypeName>` prints the full name + namespace of any type we need.
if (args.Length > 0 && args[0] == "whereis")
{
    var wanted = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var asms = new List<System.Reflection.Assembly> { typeof(CUE4Parse.FileProvider.DefaultFileProvider).Assembly };
    var convDll = Path.Combine(AppContext.BaseDirectory, "CUE4Parse-Conversion.dll");
    if (File.Exists(convDll)) asms.Add(System.Reflection.Assembly.LoadFrom(convDll));
    foreach (var a in asms)
        foreach (var t in a.GetTypes())
            if (wanted.Contains(t.Name))
                Console.WriteLine($"  {t.Name,-28} -> {t.FullName}   (asm {a.GetName().Name})");
    return;
}

// `dump <TypeName...>` prints enum values, ctors, methods and static fields.
if (args.Length > 0 && args[0] == "dump")
{
    var asms = new List<System.Reflection.Assembly> { typeof(CUE4Parse.FileProvider.DefaultFileProvider).Assembly };
    var cd = Path.Combine(AppContext.BaseDirectory, "CUE4Parse-Conversion.dll");
    if (File.Exists(cd)) asms.Add(System.Reflection.Assembly.LoadFrom(cd));
    const System.Reflection.BindingFlags Pub = System.Reflection.BindingFlags.Public;
    foreach (var tn in args.Skip(1))
    {
        var t = asms.SelectMany(a => a.GetTypes()).FirstOrDefault(x => x.Name == tn);
        if (t == null) { Console.WriteLine($"== {tn} NOT FOUND"); continue; }
        Console.WriteLine($"== {t.FullName}");
        if (t.IsEnum) { foreach (var n in Enum.GetNames(t)) Console.WriteLine("     " + n); continue; }
        foreach (var c in t.GetConstructors())
            Console.WriteLine("     ctor(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var m in t.GetMethods(Pub | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            Console.WriteLine($"     {m.ReturnType.Name} {m.Name}(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var m in t.GetMethods(Pub | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            Console.WriteLine($"     static {m.ReturnType.Name} {m.Name}(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
        foreach (var pr in t.GetProperties(Pub | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Console.WriteLine($"     prop {pr.PropertyType.Name} {pr.Name}");
    }
    return;
}

static void PrintUsage()
{
    Console.WriteLine("usage (exit codes: 0 success, 1 export/input failure, 2 usage error):");
    Console.WriteLine("  list <substring> [--exclude <substring>] [--limit N]");
    Console.WriteLine("  info");
    Console.WriteLine("  inspect <packagePath> [outJson]");
    Console.WriteLine("  dumptable <assetPath> <outJson>");
    Console.WriteLine("  exporticons <iconTableJson> <outDir> [limit]");
    Console.WriteLine("  exporttex <meshObjectPath> <outDir>             (outDir must be inside the repo root)");
    Console.WriteLine("  exporttex --all <outDir> [meshesDir]            (outDir must be inside the repo root)");
    Console.WriteLine("  exportmesh <objectPath> <outDir>");
    Console.WriteLine("  exportbps <layoutJson> <outDir>");
    Console.WriteLine("  exportall <meshrefsJson> <outDir>");
    Console.WriteLine("  findapi|whereis|dump <name...>");
    Console.WriteLine("  probe|mapapi|convapi");
}

// The render page's server root is the repository root (render_offline.mjs serves ROOT), so
// texmap values are only meaningful as repo-root-relative URLs. Locate that root the same way
// the usmap search does: walk up from the current directory and the executable.
static string? FindRepoRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "render", "modsrender.html")) ||
                Directory.Exists(Path.Combine(dir.FullName, "tooling", "PalAssetExport")))
                return dir.FullName;
        }
    }
    return null;
}

// Validates that outDir really lives inside the repo root and returns the URL prefix under the
// server root ("/out/textured/"). Previously the prefix was built from the raw argument, so an
// absolute outDir silently produced bare file names and a relative one drifted with the CWD.
// Returns false (after printing the reason) when outDir resolves outside the root.
static bool TryGetRepoUrlPrefix(string outDir, string repoRoot, out string prefix)
{
    prefix = "";
    var full = Path.GetFullPath(outDir);
    var relative = Path.GetRelativePath(repoRoot, full);
    if (relative == ".")
    {
        prefix = "/";
        return true;
    }
    if (Path.IsPathRooted(relative)
        || relative == ".."
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || relative.StartsWith("../", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"ERROR: outDir '{outDir}' resolves to '{full}', which is outside the repository root '{repoRoot}'.");
        Console.Error.WriteLine(
            "       texmap values are repo-root-relative URLs, so they cannot be expressed there; nothing was written.");
        return false;
    }
    prefix = "/" + relative.Replace('\\', '/').TrimEnd('/') + "/";
    return true;
}

// Stable, process-independent suffix for file-name collisions. string.GetHashCode() is
// randomized per process, which made reruns produce differently named PNGs and left stale
// files behind; SHA256 of the texture object path is reproducible.
static string StableSuffix(string value)
{
    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash)[..8].ToLowerInvariant();
}

var provider = new DefaultFileProvider(
    PakDir, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_1));
provider.Initialize();
provider.Mount();

// Cooked Palworld packages carry *unversioned* properties, so CUE4Parse cannot
// deserialize them without a usmap. The mappings provider attaches to the
// file provider (AbstractFileProvider.MappingsContainer), NOT to VersionContainer.
// The usmap itself came out of the running game via UE4SS's DumpUSMAP.
// Resolution order: PALWORLD_USMAP, then tooling/mappings/Palworld.usmap found by
// walking up from the current directory and from AppContext.BaseDirectory.
static string? FindUsmap()
{
    var fromEnv = Environment.GetEnvironmentVariable("PALWORLD_USMAP");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tooling", "mappings", "Palworld.usmap");
            if (File.Exists(candidate)) return candidate;
        }
    }

    return null;
}

string? MappingsPath = FindUsmap();
if (File.Exists(MappingsPath))
{
    provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(
        MappingsPath, StringComparer.OrdinalIgnoreCase);
    Console.WriteLine($"[map] usmap {new FileInfo(MappingsPath).Length:N0} bytes attached");
}
else
{
    Console.WriteLine("[map] WARNING: usmap missing -- cooked packages will fail to deserialize");
}

Console.WriteLine($"[pak] {provider.Files.Count} files mounted from Pal-Windows.pak");

switch (args[0])
{
    case "info":
        Console.WriteLine($"  files           = {provider.Files.Count}");
        Console.WriteLine($"  MountedVfs      = {provider.MountedVfs.Count}");
        Console.WriteLine($"  RequiredKeys    = {provider.RequiredKeys.Count}");
        foreach (var v in provider.MountedVfs)
            Console.WriteLine($"  vfs: {v.GetType().Name} enc={v.IsEncrypted} files={v.FileCount}");
        break;

    case "list":
    {
        var needle = args[1];
        string? exclude = null;
        int limit = 60;
        for (int i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--exclude") exclude = args[i + 1];
            if (args[i] == "--limit") limit = int.Parse(args[i + 1]);
        }

        int n = 0, total = 0;
        foreach (var k in provider.Files.Keys.OrderBy(x => x))
        {
            if (!k.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            if (exclude != null && k.Contains(exclude, StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            if (n < limit) { Console.WriteLine("  " + k); n++; }
        }
        Console.WriteLine($"  -- {total} match(es), showing {n}");
        break;
    }

    case "inspect":
    {
        var pkg = args[1];
        var outFile = args.Length > 2 ? args[2] : null;
        var objs = provider.LoadPackageObjects(pkg).ToList();
        Console.WriteLine($"  {objs.Count} object(s) in {pkg}");
        foreach (var o in objs)
            Console.WriteLine($"    {o.GetType().Name,-46} {o.Name}");
        if (outFile != null && objs.Count > 0)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(objs, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(outFile, json);
            Console.WriteLine($"  wrote {outFile} ({new FileInfo(outFile).Length:N0} bytes)");
        }
        break;
    }

    case "dumptable":
    {
        // dumptable <assetPath> <outJson>  -- generic UDataTable -> JSON (row name + properties)
        var assetPath = args[1];
        var outFile = args[2];
        var pkg = provider.LoadPackage(assetPath);
        var tables = pkg.GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Engine.UDataTable>().ToList();
        Console.WriteLine($"  tables: {tables.Count}");
        var root = new Newtonsoft.Json.Linq.JObject();
        foreach (var table in tables)
        {
            var rows = new Newtonsoft.Json.Linq.JObject();
            foreach (var kv in table.RowMap)
            {
                var row = new Newtonsoft.Json.Linq.JObject();
                var structRow = kv.Value;
                if (structRow?.Properties != null)
                {
                    foreach (var prop in structRow.Properties)
                    {
                        if (prop is not CUE4Parse.UE4.Assets.Objects.FPropertyTag tag) continue;
                        row[tag.Name.Text] = ToJToken(tag.Tag);
                    }
                }
                rows[kv.Key.Text] = row;
            }
            root[table.Name] = rows;
        }
        File.WriteAllText(outFile, root.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"  wrote {outFile} ({new FileInfo(outFile).Length:N0} bytes)");
        break;
    }

    case "exporticons":
    {
        // exporticons <iconTableJson> <outDir> [limit] -- decode SoftIcon textures to PNG
        var tableJson = args[1];
        var iconsDir = args[2];
        var limit = args.Length > 3 ? int.Parse(args[3]) : int.MaxValue;
        Directory.CreateDirectory(iconsDir);
        var table = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(tableJson));
        var tableName = table.Properties().First().Name;
        var rows = (Newtonsoft.Json.Linq.JObject)table[tableName]!;
        var mapping = new Newtonsoft.Json.Linq.JObject();
        int ok = 0, fail = 0, seen = 0;
        foreach (var prop in rows.Properties())
        {
            if (seen >= limit) break;
            var icon = (string?)prop.Value["SoftIcon"];
            if (string.IsNullOrEmpty(icon)) continue;
            seen++;
            var pkgPath = icon.Split('.')[0].Replace("/Game/", "Pal/Content/");
            try
            {
                var pkg = provider.LoadPackage(pkgPath);
                var tex = pkg.GetExports().OfType<CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D>().FirstOrDefault();
                if (tex == null) { fail++; continue; }
                var ctexture = CUE4Parse_Conversion.Textures.TextureDecoder.Decode(
                    tex, CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile);
                var bytes = CUE4Parse_Conversion.Textures.TextureEncoder.Encode(
                    ctexture, CUE4Parse_Conversion.Options.ETextureFormat.Png, false, out var encError, 100);
                if (bytes == null || bytes.Length == 0) { fail++; continue; }
                var file = Path.Combine(iconsDir, prop.Name + ".png");
                File.WriteAllBytes(file, bytes);
                mapping[prop.Name] = Path.GetFileName(file);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                if (fail <= 5) Console.WriteLine($"  icon fail {prop.Name}: {ex.Message}");
            }
        }
        File.WriteAllText(Path.Combine(iconsDir, "_icons.json"), mapping.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"  icons ok={ok} fail={fail} dir={iconsDir}");
        break;
    }

    case "exporttex":
    {
        // exporttex <meshObjectPath> <outDir>             -- one mesh (single mode)
        // exporttex --all <outDir> [meshesDir]            -- every mesh of <meshesDir>/_meshrefs.json
        //                                                    plus two merged texmaps (batch mode)
        // outDir must resolve inside the repository root: texmap values are repo-root-relative
        // URLs for the render page (render_offline.mjs serves the repo root). Exit 2 otherwise.
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("ERROR: could not locate the repository root (looked for render/modsrender.html or tooling/PalAssetExport, walking up from the CWD and the executable).");
            Console.Error.WriteLine("       texmap values must be repo-root-relative URLs, so nothing was written.");
            Environment.ExitCode = 2;
            break;
        }

        if (args[1] == "--all")
        {
            var batchDir = args[2];
            var meshDir = args.Length > 3 ? args[3] : "meshes";
            if (!TryGetRepoUrlPrefix(batchDir, repoRoot, out var batchUrlPrefix))
            {
                Environment.ExitCode = 2;
                break;
            }

            // Validate the input before creating anything: a missing or garbled _meshrefs.json
            // used to leave an empty outDir behind and still exit 0.
            var refsPath = Path.Combine(meshDir, "_meshrefs.json");
            if (!File.Exists(refsPath))
            {
                Console.Error.WriteLine($"ERROR: meshrefs file not found: {refsPath}");
                Console.Error.WriteLine("       run `exportbps` first, or pass [meshesDir] as the 4th argument; nothing was written.");
                Environment.ExitCode = 1;
                break;
            }
            Newtonsoft.Json.Linq.JObject refs;
            try
            {
                refs = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(refsPath));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: cannot parse {refsPath}: {ex.GetType().Name}: {ex.Message}; nothing was written.");
                Environment.ExitCode = 1;
                break;
            }

            Directory.CreateDirectory(batchDir);

            // Parameter names differ per material family: stone/wood use "Base Texture"/"Normal Map"/
            // "MetallicRoughnessOcclusionSpecularTexture", glass uses "BaseColorMap"/"NormalMap"/
            // "MaskMap". Accept both. "ChangeColor Mask"/"PM" paint masks are NOT the ORM map.
            var batchBaseNames = new[] { "Base Texture", "BaseColorMap", "BaseColor", "Diffuse" };
            var batchNormalNames = new[] { "Normal Map", "NormalMap", "Normal" };
            var batchOrmNames = new[] { "MetallicRoughnessOcclusionSpecularTexture", "MaskMap", "ORM", "MetallicRoughness" };

            var safeMap = new Newtonsoft.Json.Linq.JObject();
            var ormMap = new Newtonsoft.Json.Linq.JObject();
            var emitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // texture path -> url
            var fileOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // file name -> texture path
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);            // material names
            var warnings = new List<string>();                                                 // non-fatal (name collisions)
            // Failures are written to <batchDir>/_failures.json and force exit code 1; a run never
            // reports success while texmap entries are missing.
            var failures = new List<Newtonsoft.Json.Linq.JObject>();
            void AddFailure(string kind, string context, string objectName, string reason)
                => failures.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["kind"] = kind,
                    ["context"] = context,
                    ["object"] = objectName,
                    ["reason"] = reason,
                });

            byte[]? BatchEncodePng(CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D tex)
            {
                var ctexture = CUE4Parse_Conversion.Textures.TextureDecoder.Decode(
                    tex, CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile);
                if (ctexture == null) return null;
                return CUE4Parse_Conversion.Textures.TextureEncoder.Encode(
                    ctexture, CUE4Parse_Conversion.Options.ETextureFormat.Png, false, out _, 100);
            }

            // Decode one texture to <batchDir>/<TextureName>.png, deduped by texture object path, and
            // return its URL. Returns null on encode failure.
            string? EmitPng(CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D tex, ref long bytesOut, out long sizeOut)
            {
                sizeOut = 0;
                var texObjPath = tex.GetPathName();
                if (emitted.TryGetValue(texObjPath, out var known))
                {
                    var existing = Path.Combine(batchDir, Path.GetFileName(known));
                    if (File.Exists(existing)) sizeOut = new FileInfo(existing).Length;
                    return known;
                }

                var bytes = BatchEncodePng(tex);
                if (bytes == null || bytes.Length == 0) return null;

                var fileName = tex.Name + ".png";
                if (fileOwners.TryGetValue(fileName, out var owner) && !string.Equals(owner, texObjPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Two different textures with the same short name: disambiguate with a stable
                    // hash instead of letting one silently overwrite/alias the other.
                    fileName = tex.Name + "_" + StableSuffix(texObjPath) + ".png";
                    var msg = $"name collision '{tex.Name}' -> '{fileName}' (also used by {owner})";
                    warnings.Add(msg);
                    Console.WriteLine($"    WARNING: {msg}");
                }
                fileOwners[fileName] = texObjPath;
                File.WriteAllBytes(Path.Combine(batchDir, fileName), bytes);
                bytesOut += bytes.Length;
                sizeOut = bytes.Length;
                var url = batchUrlPrefix + fileName;
                emitted[texObjPath] = url;
                return url;
            }

            // Resolve one material instance's three maps into both texmaps. Returns the tally so the
            // caller can aggregate without capturing ref parameters (C# forbids that in local funcs).
            (int Ok, int Fail, int Missing, long Bytes, string Detail) ProcessMaterial(
                CUE4Parse.UE4.Assets.Exports.UObject matObj,
                string origin)
            {
                var matName = matObj.Name;
                if (matObj is not CUE4Parse.UE4.Assets.Exports.Material.UMaterialInstanceConstant mic)
                {
                    var why = $"'{matName}' is {matObj.GetType().Name}, not UMaterialInstanceConstant -- no TextureParameterValues";
                    AddFailure("material", origin, matName, why);
                    return (0, 1, 0, 0, why);
                }

                CUE4Parse.UE4.Objects.UObject.FPackageIndex? baseIdx = null, normalIdx = null, ormIdx = null;
                foreach (var tp in mic.TextureParameterValues)
                {
                    var pName = tp.ParameterInfo.Name.Text;
                    if (baseIdx == null && batchBaseNames.Contains(pName, StringComparer.OrdinalIgnoreCase)) baseIdx = tp.ParameterValue;
                    else if (normalIdx == null && batchNormalNames.Contains(pName, StringComparer.OrdinalIgnoreCase)) normalIdx = tp.ParameterValue;
                    else if (ormIdx == null && batchOrmNames.Contains(pName, StringComparer.OrdinalIgnoreCase)) ormIdx = tp.ParameterValue;
                }

                var safeEntry = new Newtonsoft.Json.Linq.JObject();
                var ormEntry = new Newtonsoft.Json.Linq.JObject();
                var labels = new List<string>();
                int ok = 0, fail = 0, missing = 0;
                long bytes = 0;

                void Load(string role, Newtonsoft.Json.Linq.JObject safe, Newtonsoft.Json.Linq.JObject orm)
                {
                    var idx = role switch { "base" => baseIdx, "normal" => normalIdx, _ => ormIdx };
                    if (idx == null || idx.IsNull)
                    {
                        missing++;
                        var avail = string.Join(", ", mic.TextureParameterValues.Select(t => t.ParameterInfo.Name.Text));
                        var why = $"no {role} parameter (has: {avail})";
                        AddFailure("texture", origin, matName, why);
                        labels.Add($"{role}=<absent>");
                        return;
                    }
                    if (idx.Load() is not CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D tex)
                    {
                        fail++;
                        var why = $"{role} value is not a UTexture2D";
                        AddFailure("texture", origin, matName, why);
                        labels.Add($"{role}=<not UTexture2D>");
                        return;
                    }
                    var url = EmitPng(tex, ref bytes, out var size);
                    if (url == null)
                    {
                        fail++;
                        var why = $"{role} '{tex.Name}' failed to encode";
                        AddFailure("texture", origin, matName, why);
                        labels.Add($"{role}={tex.Name}<encode failed>");
                        return;
                    }
                    ok++;
                    labels.Add($"{role}={tex.Name}({size:N0}B)");
                    if (role == "base") { safe["map"] = url; orm["map"] = url; }
                    else if (role == "normal") { safe["normalMap"] = url; orm["normalMap"] = url; }
                    else { orm["roughnessMap"] = url; orm["metalnessMap"] = url; } // raw ORM: packing unconfirmed
                }

                Load("base", safeEntry, ormEntry);
                Load("normal", safeEntry, ormEntry);
                Load("orm", safeEntry, ormEntry);

                // The key is written even when a channel is absent, so the texmap key set can match
                // the GLB material union; the absent channel is reported, never hidden.
                safeMap[matName] = safeEntry;
                ormMap[matName] = ormEntry;
                processed.Add(matName);

                return (ok, fail, missing, bytes, $"{matName}  {string.Join("  ", labels)}");
            }

            List<string> GlbMaterialNames(string glbPath)
            {
                var list = new List<string>();
                try
                {
                    var b = File.ReadAllBytes(glbPath);
                    if (b.Length < 20 || b[0] != (byte)'g' || b[1] != (byte)'l' || b[2] != (byte)'T' || b[3] != (byte)'F')
                    {
                        AddFailure("glb", glbPath, Path.GetFileName(glbPath), "not a GLB file (bad magic or short header)");
                        return list;
                    }
                    var jsonLen = (int)BitConverter.ToUInt32(b, 12);
                    if (jsonLen <= 0 || jsonLen > b.Length - 20)
                    {
                        AddFailure("glb", glbPath, Path.GetFileName(glbPath), "truncated or invalid GLB JSON chunk");
                        return list;
                    }
                    var o = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jsonLen));
                    if (o["materials"] is Newtonsoft.Json.Linq.JArray mats)
                        foreach (var m in mats)
                        {
                            var nm = (string?)m["name"];
                            if (!string.IsNullOrEmpty(nm)) list.Add(nm!);
                        }
                }
                catch (Exception ex)
                {
                    AddFailure("glb", glbPath, Path.GetFileName(glbPath), $"{ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"  GLB read failed {Path.GetFileName(glbPath)}: {ex.Message}");
                }
                return list;
            }

            // ---- pass 1: every mesh reachable from _meshrefs.json ----
            var meshJobs = new List<(string Bp, string ObjPath)>();
            int refKeysWithoutMeshes = 0;
            foreach (var prop in refs.Properties())
            {
                if (prop.Value["meshes"] is not Newtonsoft.Json.Linq.JArray meshArr) { refKeysWithoutMeshes++; continue; }
                foreach (var mm in meshArr)
                {
                    var op = (string?)mm["object_path"];
                    if (!string.IsNullOrEmpty(op)) meshJobs.Add((prop.Name, op!));
                }
            }
            Console.WriteLine($"  _meshrefs.json: {refs.Count} key(s), {meshJobs.Count} mesh object path(s), {refKeysWithoutMeshes} without meshes (skipped)");

            int meshOkCount = 0, meshFailCount = 0;
            foreach (var job in meshJobs)
            {
                try
                {
                    var obj = provider.LoadPackageObject(job.ObjPath) ?? provider.SafeLoadPackageObject(job.ObjPath);
                    if (obj is not CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh mesh)
                    {
                        meshFailCount++;
                        AddFailure("mesh", $"[{job.Bp}]", job.ObjPath, "not a UStaticMesh");
                        Console.WriteLine($"  [{job.Bp}] {job.ObjPath}: not a UStaticMesh -- skipped");
                        continue;
                    }
                    var meshSlots = mesh.StaticMaterials ?? Array.Empty<CUE4Parse.UE4.Assets.Exports.StaticMesh.FStaticMaterial>();
                    int mOk = 0, mFail = 0, mMissing = 0;
                    long mBytes = 0;
                    var names = new List<string>();
                    foreach (var slot in meshSlots)
                    {
                        var iface = slot.MaterialInterface;
                        if (iface == null || iface.IsNull)
                        {
                            AddFailure("mesh", mesh.Name, slot.MaterialSlotName.Text, "null material interface");
                            Console.WriteLine($"    [{mesh.Name}] slot '{slot.MaterialSlotName.Text}': null material interface");
                            continue;
                        }
                        var matObj = iface.Load();
                        if (matObj == null)
                        {
                            mFail++;
                            AddFailure("material", mesh.Name, slot.MaterialSlotName.Text, "material slot did not load");
                            continue;
                        }
                        if (processed.Contains(matObj.Name)) { if (!names.Contains(matObj.Name + "(dedup)")) names.Add(matObj.Name + "(dedup)"); continue; }
                        names.Add(matObj.Name);
                        var r = ProcessMaterial(matObj, mesh.Name);
                        mOk += r.Ok; mFail += r.Fail; mMissing += r.Missing; mBytes += r.Bytes;
                        Console.WriteLine($"    {r.Detail}");
                    }
                    meshOkCount++;
                    Console.WriteLine($"  [{job.Bp}] {mesh.Name}: {meshSlots.Length} slot(s), {names.Count} material(s) [{string.Join(", ", names)}] tex ok={mOk} fail={mFail} absent={mMissing} {mBytes:N0} B");
                }
                catch (Exception ex)
                {
                    meshFailCount++;
                    AddFailure("mesh", $"[{job.Bp}]", job.ObjPath, $"{ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"  [{job.Bp}] {job.ObjPath}: ERROR {ex.GetType().Name}: {ex.Message}");
                }
            }

            // ---- pass 2: materials present in meshes/*.glb but not reachable from _meshrefs.json ----
            var glbUnion = new List<string>();
            var glbByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var glb in Directory.GetFiles(meshDir, "*.glb").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var mats = GlbMaterialNames(glb);
                glbByName[Path.GetFileNameWithoutExtension(glb)] = mats;
                foreach (var m in mats) if (!glbUnion.Contains(m)) glbUnion.Add(m);
            }
            Console.WriteLine($"  {meshDir}/*.glb: {glbByName.Count} GLB(s), union of materials[].name = {glbUnion.Count}");

            Dictionary<string, string>? miIndex = null;
            string? FindMiPath(string name)
            {
                if (miIndex == null)
                {
                    miIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var k in provider.Files.Keys)
                    {
                        if (!k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
                        var fn = k[(k.LastIndexOf('/') + 1)..^".uasset".Length];
                        if (!fn.StartsWith("MI_", StringComparison.OrdinalIgnoreCase)) continue;
                        miIndex[fn] = k[..^".uasset".Length];
                    }
                }
                return miIndex.TryGetValue(name, out var p) ? p : null;
            }

            foreach (var kv in glbByName)
            {
                foreach (var matName in kv.Value)
                {
                    if (processed.Contains(matName)) continue;
                    var miPath = FindMiPath(matName);
                    if (miPath == null)
                    {
                        AddFailure("glb", kv.Key, matName, "in GLB but no MI_ package found in pak");
                        Console.WriteLine($"  [glb-only] {kv.Key} -> {matName}: no MI_ package in pak -- UNRESOLVED");
                        continue;
                    }
                    try
                    {
                        var matObj = provider.LoadPackageObject(miPath) ?? provider.SafeLoadPackageObject(miPath);
                        if (matObj == null)
                        {
                            AddFailure("glb", kv.Key, matName, $"{miPath} did not load");
                            Console.WriteLine($"  [glb-only] {kv.Key} -> {matName}: load failed -- UNRESOLVED");
                            continue;
                        }
                        int gOk = 0, gFail = 0, gMissing = 0;
                        long gBytes = 0;
                        var gr = ProcessMaterial(matObj, $"glb-only:{kv.Key}");
                        gOk += gr.Ok; gFail += gr.Fail; gMissing += gr.Missing; gBytes += gr.Bytes;
                        Console.WriteLine($"    {gr.Detail}");
                        Console.WriteLine($"  [glb-only] {kv.Key} -> {matName}: not in _meshrefs.json, resolved via {miPath[(miPath.LastIndexOf('/') + 1)..]} tex ok={gOk} fail={gFail} absent={gMissing} {gBytes:N0} B");
                    }
                    catch (Exception ex)
                    {
                        AddFailure("glb", kv.Key, matName, $"{ex.GetType().Name}: {ex.Message}");
                        Console.WriteLine($"  [glb-only] {kv.Key} -> {matName}: ERROR {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // ---- emit merged texmaps and prove the key sets ----
            var safePath = Path.Combine(batchDir, "_texmap_all.json");
            var ormPath = Path.Combine(batchDir, "_texmap_all_orm.json");
            File.WriteAllText(safePath, safeMap.ToString(Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(ormPath, ormMap.ToString(Newtonsoft.Json.Formatting.Indented));

            var unionSet = glbUnion.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var safeKeys = safeMap.Properties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ormKeys = ormMap.Properties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Count only the files this run produced (the caller's dir may hold unrelated PNGs),
            // and only ones that really exist on disk.
            var payloadFiles = emitted.Values
                .Select(u => Path.Combine(batchDir, Path.GetFileName(u)))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            long payloadBytes = payloadFiles.Sum(f => new FileInfo(f).Length);

            Console.WriteLine();
            Console.WriteLine($"  meshes processed: ok={meshOkCount} fail={meshFailCount} of {meshJobs.Count}");
            Console.WriteLine($"  materials resolved: {processed.Count} of {glbUnion.Count} in GLB union");
            Console.WriteLine($"  _texmap_all.json      keys={safeKeys.Count}  setEqualToGlbUnion={unionSet.SetEquals(safeKeys)}  missingKeys=[{string.Join(", ", unionSet.Except(safeKeys))}]");
            Console.WriteLine($"  _texmap_all_orm.json  keys={ormKeys.Count}  setEqualToGlbUnion={unionSet.SetEquals(ormKeys)}  missingKeys=[{string.Join(", ", unionSet.Except(ormKeys))}]");
            Console.WriteLine($"  payload: {payloadFiles.Count} PNG in {batchDir}, {payloadBytes:N0} B ({payloadBytes / 1048576.0:N1} MB)");
            if (warnings.Count > 0)
            {
                Console.WriteLine($"  {warnings.Count} warning(s):");
                foreach (var w in warnings) Console.WriteLine($"    - {w}");
            }

            // Failure report: the run must not look successful while texmap entries are missing.
            var failuresPath = Path.Combine(batchDir, "_failures.json");
            if (failures.Count > 0)
            {
                var failureDoc = new Newtonsoft.Json.Linq.JObject
                {
                    ["command"] = "exporttex --all",
                    ["generated"] = DateTimeOffset.Now.ToString("o"),
                    ["failures"] = new Newtonsoft.Json.Linq.JArray(failures),
                };
                File.WriteAllText(failuresPath, failureDoc.ToString(Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine($"  {failures.Count} failure(s) reported -- wrote {failuresPath}");
                foreach (var f in failures)
                    Console.WriteLine($"    - [{f["kind"]}] {f["context"]} {f["object"]}: {f["reason"]}");
                Environment.ExitCode = 1;
            }
            else
            {
                try { if (File.Exists(failuresPath)) File.Delete(failuresPath); } catch { /* stale report is inert */ }
                Console.WriteLine("  no failures: texmaps are complete for the GLB material union");
            }
            Console.WriteLine($"  wrote {safePath}");
            Console.WriteLine($"  wrote {ormPath}");
            break;
        }

        // ---- single-mesh mode ----
        // exporttex <meshObjectPath> <outDir> -- decode one static mesh's base-colour and normal
        // textures to PNG, plus <outDir>/_texmap.json keyed by the glTF material name.
        var objPath = args[1];
        var texDir = args[2];
        if (!TryGetRepoUrlPrefix(texDir, repoRoot, out var urlPrefix))
        {
            Environment.ExitCode = 2;
            break;
        }

        CUE4Parse.UE4.Assets.Exports.UObject? meshObj;
        try
        {
            meshObj = provider.LoadPackageObject(objPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: cannot load '{objPath}': {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine("       nothing was written");
            Environment.ExitCode = 1;
            break;
        }
        Console.WriteLine($"  loaded: {meshObj?.GetType().Name}  {meshObj?.Name}");
        if (meshObj is not CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh sm)
        {
            Console.Error.WriteLine("ERROR: not a UStaticMesh -- nothing written");
            Environment.ExitCode = 1;
            break;
        }
        // Only create the output directory once the input resolved (no empty dirs on bad input).
        Directory.CreateDirectory(texDir);

        // Parameter names differ per material family: stone/wood use "Base Texture"/"Normal Map",
        // glass and Japanese style use "BaseColorMap"/"MaskMap"/"NormalMap". Accept both.
        var baseParamNames = new[] { "Base Texture", "BaseColorMap", "BaseColor", "Diffuse" };
        var normalParamNames = new[] { "Normal Map", "NormalMap", "Normal" };

        var texmap = new Newtonsoft.Json.Linq.JObject();
        var emittedTex = new Dictionary<string, (string Url, string FileName)>(StringComparer.OrdinalIgnoreCase); // texture object path -> what we wrote
        var texFileOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);                     // file name -> texture object path
        var texWarnings = new List<string>();
        int ok = 0, fail = 0;

        byte[]? EncodePng(CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D tex)
        {
            var ctexture = CUE4Parse_Conversion.Textures.TextureDecoder.Decode(
                tex, CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile);
            if (ctexture == null) return null;
            return CUE4Parse_Conversion.Textures.TextureEncoder.Encode(
                ctexture, CUE4Parse_Conversion.Options.ETextureFormat.Png, false, out _, 100);
        }

        // Decode tex to <texDir>/<TextureName>.png, deduped by texture OBJECT PATH (not by short
        // name), and return its URL. Different packages can hold textures with the same short
        // name; those get the same stable "_<sha256 prefix>" suffix the batch mode uses instead
        // of silently letting one overwrite or alias the other. Returns null when nothing was
        // written, and in that case the caller must not register a texmap entry.
        string? EmitTexture(CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D tex, out long sizeOut)
        {
            sizeOut = 0;
            var texObjPath = tex.GetPathName();
            if (emittedTex.TryGetValue(texObjPath, out var known))
            {
                var existing = Path.Combine(texDir, known.FileName);
                if (File.Exists(existing)) sizeOut = new FileInfo(existing).Length;
                return known.Url;
            }

            var bytes = EncodePng(tex);
            if (bytes == null || bytes.Length == 0) return null;

            var fileName = tex.Name + ".png";
            if (texFileOwners.TryGetValue(fileName, out var owner) && !string.Equals(owner, texObjPath, StringComparison.OrdinalIgnoreCase))
            {
                fileName = tex.Name + "_" + StableSuffix(texObjPath) + ".png";
                var msg = $"texture name collision '{tex.Name}': '{texObjPath}' and '{owner}' both want '{tex.Name}.png'; disambiguated to '{fileName}'";
                texWarnings.Add(msg);
                Console.WriteLine($"    WARNING: {msg}");
            }
            texFileOwners[fileName] = texObjPath;
            File.WriteAllBytes(Path.Combine(texDir, fileName), bytes);
            sizeOut = bytes.Length;
            var url = urlPrefix + fileName;
            emittedTex[texObjPath] = (url, fileName);
            return url;
        }

        var slots = sm.StaticMaterials ?? Array.Empty<CUE4Parse.UE4.Assets.Exports.StaticMesh.FStaticMaterial>();
        Console.WriteLine($"  {slots.Length} material slot(s)");
        foreach (var slot in slots)
        {
            var slotName = slot.MaterialSlotName.Text;
            var iface = slot.MaterialInterface;
            if (iface == null || iface.IsNull)
            {
                Console.WriteLine($"  [{slotName}] null material interface -- skipped");
                continue;
            }

            var matObj = iface.Load();
            if (matObj == null)
            {
                fail++;
                Console.WriteLine($"  [{slotName}] material could not be loaded -- skipped");
                continue;
            }

            // CUE4Parse's glTF writer names the exported glTF material after the material object
            // itself, so this is the key the renderer's texmap has to use -- not the slot name.
            var gltfMatName = matObj.Name;
            Console.WriteLine($"  [{slotName}] material {matObj.GetType().Name} '{gltfMatName}'");

            if (matObj is not CUE4Parse.UE4.Assets.Exports.Material.UMaterialInstanceConstant mic)
            {
                fail++;
                Console.WriteLine("    not a UMaterialInstanceConstant -- no TextureParameterValues");
                continue;
            }

            CUE4Parse.UE4.Objects.UObject.FPackageIndex? baseIdx = null, normalIdx = null;
            var baseParam = "";
            var normalParam = "";
            foreach (var tp in mic.TextureParameterValues)
            {
                var pName = tp.ParameterInfo.Name.Text;
                if (baseIdx == null && baseParamNames.Contains(pName, StringComparer.OrdinalIgnoreCase))
                {
                    baseIdx = tp.ParameterValue;
                    baseParam = pName;
                }
                else if (normalIdx == null && normalParamNames.Contains(pName, StringComparer.OrdinalIgnoreCase))
                {
                    normalIdx = tp.ParameterValue;
                    normalParam = pName;
                }
            }

            if (baseIdx != null && !baseIdx.IsNull)
            {
                var tex = baseIdx.Load() as CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D;
                if (tex == null)
                {
                    fail++;
                    Console.WriteLine($"    base '{baseParam}': not a UTexture2D -- skipped");
                }
                else
                {
                    try
                    {
                        var url = EmitTexture(tex, out var size);
                        if (url == null)
                        {
                            fail++;
                            Console.WriteLine($"    base   '{baseParam}' {tex.Name}: encode produced nothing");
                        }
                        else
                        {
                            // Only the base colour goes into _texmap.json: the render page applies
                            // texmap entries as MeshStandardMaterial.map (colour), not as normalMap.
                            texmap[gltfMatName] = url;
                            ok++;
                            var filePath = Path.Combine(texDir, Path.GetFileName(url));
                            Console.WriteLine($"    base   '{baseParam}' {tex.Name} -> {size:N0} B  {filePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        Console.WriteLine($"    base   '{baseParam}' {tex.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                fail++;
                Console.WriteLine("    no base-colour parameter found in: " +
                    string.Join(", ", mic.TextureParameterValues.Select(t => t.ParameterInfo.Name.Text)));
            }

            // Normal map: decoded for evidence only -- the render page has no normalMap hook yet.
            if (normalIdx != null && !normalIdx.IsNull)
            {
                var ntex = normalIdx.Load() as CUE4Parse.UE4.Assets.Exports.Texture.UTexture2D;
                if (ntex == null)
                {
                    Console.WriteLine($"    normal '{normalParam}': not a UTexture2D -- skipped");
                }
                else
                {
                    try
                    {
                        var url = EmitTexture(ntex, out var nsize);
                        if (url == null)
                        {
                            Console.WriteLine($"    normal '{normalParam}' {ntex.Name}: encode produced nothing");
                        }
                        else
                        {
                            var nfilePath = Path.Combine(texDir, Path.GetFileName(url));
                            Console.WriteLine($"    normal '{normalParam}' {ntex.Name} -> {nsize:N0} B  {nfilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    normal '{normalParam}' {ntex.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        var texmapPath = Path.Combine(texDir, "_texmap.json");
        File.WriteAllText(texmapPath, texmap.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"  textures ok={ok} fail={fail} dir={texDir}");
        if (texWarnings.Count > 0)
        {
            Console.WriteLine($"  {texWarnings.Count} warning(s):");
            foreach (var w in texWarnings) Console.WriteLine($"    - {w}");
        }
        if (fail > 0)
        {
            // Incomplete texmap: keep the partial result for inspection but never report success.
            Console.Error.WriteLine($"ERROR: {fail} texture/channel failure(s) -- _texmap.json is incomplete");
            Environment.ExitCode = 1;
        }
        Console.WriteLine($"  wrote {texmapPath}");
        break;
    }

    case "exportmesh":
    {
        var objPath = args[1];
        var outDir = args[2];
        Directory.CreateDirectory(outDir);

        var obj = provider.LoadPackageObject(objPath);
        Console.WriteLine($"  loaded: {obj?.GetType().Name}  {obj?.Name}");
        if (obj is not CUE4Parse.UE4.Assets.Exports.StaticMesh.UStaticMesh sm)
        {
            Console.WriteLine("  not a UStaticMesh -- aborting");
            break;
        }

        var opts = new CUE4Parse_Conversion.Options.ExportOptions(
            CUE4Parse_Conversion.Options.EMeshFormat.Gltf2,
            CUE4Parse_Conversion.Options.ENaniteMeshFormat.NoNanite,
            CUE4Parse_Conversion.Options.EMeshQuality.Highest,
            CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile,
            CUE4Parse_Conversion.Options.ETextureFormat.Png,
            100, false, false,
            CUE4Parse.UE4.Assets.Exports.Material.EMaterialDepth.TopLayerOnly,
            false, false,
            CUE4Parse_Conversion.Options.ESocketFormat.None,
            CUE4Parse_Conversion.Writers.UEFormat.Enums.EFileCompressionFormat.None);

        var session = new CUE4Parse_Conversion.ExportSession(null);
        session.Add(sm);
        var written = await session.RunAsync(outDir, opts, null, CancellationToken.None);
        Console.WriteLine($"  exported {written.Count} file(s):");
        foreach (var f in written)
            Console.WriteLine($"    {f.GetType().Name}  {f}");
        foreach (var f in Directory.GetFiles(outDir))
            Console.WriteLine($"    on disk: {Path.GetFileName(f)}  {new FileInfo(f).Length:N0} B");
        break;
    }

    case "exportbps":
    {
        // exportbps <layoutJson> <outDir>
        // Resolves every distinct `bp` in the layout to its StaticMesh and exports
        // one <bp>.glb per building type, plus a _bboxes.json manifest.
        var layoutPath = args[1];
        var outDir = args[2];
        Directory.CreateDirectory(outDir);

        var layout = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(layoutPath));
        var bps = layout["buildings"]!.Select(b => (string)b["bp"]!).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"  {bps.Count} distinct building blueprint(s) in layout");

        const string BpDir = "Pal/Content/Pal/Blueprint/MapObject/BuildObject/";

        // MapObjectId -> real blueprint name. Palworld does NOT derive the BP name
        // mechanically from the MapObjectId; these are the observed divergences,
        // confirmed against the live game's actor class names.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wooden_foundation"] = "Wood_Foundation",
            ["Wooden_wall"] = "Wood_Wall_V2",
            ["Wooden_roof"] = "Wood_Roof",
            ["ElectricGenerator"] = "EnergyGenerator_Electric",
            ["Heater"] = "HeaterMedieval",
        };

        var packageNames = provider.Files.Keys
            .Where(k => k.StartsWith(BpDir, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Select(k => k[BpDir.Length..^".uasset".Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // BuildObject/ has subdirectories (Furniture/, MultiProduct/, LabResearchSpace/, Base/,
        // ForDevelop/). Keys under them are "Furniture/BP_BuildObject_Chair01_Wood", which the
        // exact-name checks below could never match -- that silently dropped 219/481 (45%) of the
        // catalog on the first full run. Index by BARE package name as well, preferring top level.
        var byBare = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in packageNames)
        {
            var barePkg = p[(p.LastIndexOf('/') + 1)..];
            if (!byBare.TryGetValue(barePkg, out var seen) || seen.Contains('/')) byBare[barePkg] = p;
        }
        Console.WriteLine($"  {packageNames.Count} build-object blueprints available in the pak"
                          + $" ({packageNames.Count(n => n.Contains('/'))} of them in subdirectories)");

        string? ResolveBp(string bp)
        {
            var bare = bp.StartsWith("BP_BuildObject_") ? bp["BP_BuildObject_".Length..] : bp;
            if (packageNames.Contains(bp)) return bp;
            if (aliases.TryGetValue(bare, out var alias) && byBare.TryGetValue("BP_BuildObject_" + alias, out var viaAlias))
                return viaAlias;
            if (byBare.TryGetValue("BP_BuildObject_" + bare, out var viaName)) return viaName;
            return null;
        }

        var manifest = new Newtonsoft.Json.Linq.JObject();
        int okCount = 0, failCount = 0;

        foreach (var bp in bps)
        {
            try
            {
                var realBp = ResolveBp(bp);
                if (realBp == null)
                {
                    Console.WriteLine($"  [{bp}] -> no matching blueprint in pak");
                    manifest[bp] = new Newtonsoft.Json.Linq.JObject { ["error"] = "no matching blueprint" };
                    failCount++;
                    continue;
                }

                var objs = provider.LoadPackageObjects(BpDir + realBp).ToList();
                var refs = new List<string>();
                foreach (var o in objs)
                {
                    // Any object carrying a StaticMesh property -- this catches
                    // StaticMeshComponent subclasses that a name check would miss.
                    CUE4Parse.UE4.Objects.UObject.FPackageIndex? idx = null;
                    try { idx = o.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("StaticMesh"); }
                    catch { /* object without that property */ }
                    if (idx == null || idx.IsNull) continue;
                    var m = idx.Load();
                    if (m == null) continue;
                    var path = m.GetPathName();
                    if (!refs.Contains(path)) refs.Add(path);
                }

                if (refs.Count == 0)
                {
                    Console.WriteLine($"  [{bp}] -> {realBp}: no StaticMesh property found ({objs.Count} objects)");
                    failCount++;
                    manifest[bp] = new Newtonsoft.Json.Linq.JObject { ["blueprint"] = realBp, ["error"] = "no static mesh ref" };
                    continue;
                }

                var meshes = new List<Newtonsoft.Json.Linq.JObject>();
                foreach (var r in refs)
                {
                    var objPath = r.Split('.')[0];
                    var meshObj = provider.LoadPackageObject(objPath) ?? provider.SafeLoadPackageObject(objPath);
                    if (meshObj == null) { Console.WriteLine($"  [{bp}] mesh not loadable: {r}"); continue; }
                    meshes.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["object_path"] = objPath,
                        ["name"] = meshObj.Name,
                    });
                }
                manifest[bp] = new Newtonsoft.Json.Linq.JObject
                {
                    ["blueprint"] = realBp,
                    ["meshes"] = new Newtonsoft.Json.Linq.JArray(meshes),
                };
                var tag = realBp.Equals(bp, StringComparison.OrdinalIgnoreCase) ? "" : $"  (renamed from {bp})";
                Console.WriteLine($"  [{bp}] {refs.Count} mesh(es): {string.Join(", ", refs.Select(x => x.Split('/').Last()))}{tag}");
                okCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{bp}] ERROR {ex.GetType().Name}: {ex.Message}");
                manifest[bp] = new Newtonsoft.Json.Linq.JObject { ["error"] = ex.Message };
                failCount++;
            }
        }

        File.WriteAllText(Path.Combine(outDir, "_meshrefs.json"), manifest.ToString(Newtonsoft.Json.Formatting.Indented));
        Console.WriteLine($"\n  resolved {okCount} ok / {failCount} failed");
        Console.WriteLine($"  wrote {Path.Combine(outDir, "_meshrefs.json")}");
        break;
    }

    case "exportall":
    {
        // exportall <meshrefsJson> <outDir>
        // Exports every resolved mesh, then promotes the biggest one per building
        // type to <outDir>/<bp>.glb (what the viewer loads) while keeping every
        // part under <outDir>/<bp>/ for later composition.
        var refsPath = args[1];
        var outDir = args[2];
        Directory.CreateDirectory(outDir);

        var refs = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(refsPath));
        var staging = Path.Combine(outDir, "_staging_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);

        var opts = new CUE4Parse_Conversion.Options.ExportOptions(
            CUE4Parse_Conversion.Options.EMeshFormat.Gltf2,
            CUE4Parse_Conversion.Options.ENaniteMeshFormat.NoNanite,
            CUE4Parse_Conversion.Options.EMeshQuality.Highest,
            CUE4Parse.UE4.Assets.Exports.Texture.ETexturePlatform.DesktopMobile,
            CUE4Parse_Conversion.Options.ETextureFormat.Png,
            100, false, false,
            CUE4Parse.UE4.Assets.Exports.Material.EMaterialDepth.TopLayerOnly,
            true, false,
            CUE4Parse_Conversion.Options.ESocketFormat.None,
            CUE4Parse_Conversion.Writers.UEFormat.Enums.EFileCompressionFormat.None);

        var report = new Newtonsoft.Json.Linq.JObject();
        int total = 0, exportedCount = 0;

        foreach (var prop in refs.Properties())
        {
            var bp = prop.Name;
            var bpDir = Path.Combine(outDir, bp);
            Directory.CreateDirectory(bpDir);

            var meshList = prop.Value["meshes"] as Newtonsoft.Json.Linq.JArray;
            if (meshList == null) { report[bp] = "no meshes"; continue; }

            var produced = new List<(string name, string file, long size)>();
            foreach (var m in meshList)
            {
                var objPath = (string?)m["object_path"];
                if (objPath == null) continue;
                total++;
                try
                {
                    var obj = provider.LoadPackageObject(objPath) ?? provider.SafeLoadPackageObject(objPath);
                    if (obj == null) { Console.WriteLine($"  [{bp}] load failed: {objPath}"); continue; }

                    var oneDir = Path.Combine(staging, Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(oneDir);

                    var session = new CUE4Parse_Conversion.ExportSession(null);
                    session.Add(obj);
                    await session.RunAsync(oneDir, opts, null, CancellationToken.None);

                    var got = Directory.GetFiles(oneDir, "*.glb", SearchOption.AllDirectories);
                    if (got.Length == 0) { Console.WriteLine($"  [{bp}] no glb produced for {objPath}"); continue; }

                    var src = got.OrderByDescending(f => new FileInfo(f).Length).First();
                    var meshName = obj.Name;
                    var dest = Path.Combine(bpDir, meshName + ".glb");
                    File.Copy(src, dest, true);
                    produced.Add((meshName, dest, new FileInfo(dest).Length));
                    exportedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [{bp}] ERROR {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (produced.Count == 0) { report[bp] = "export produced nothing"; continue; }

            var best = produced.OrderByDescending(p => p.size).First();
            File.Copy(best.file, Path.Combine(outDir, bp + ".glb"), true);
            report[bp] = new Newtonsoft.Json.Linq.JObject
            {
                ["primary"] = best.name,
                ["primary_file"] = bp + ".glb",
                ["parts"] = new Newtonsoft.Json.Linq.JArray(produced.Select(p => p.name)),
            };
            Console.WriteLine($"  [{bp}] {produced.Count} part(s), primary={best.name} ({best.size:N0} B)");
        }

        File.WriteAllText(Path.Combine(outDir, "_parts.json"), report.ToString(Newtonsoft.Json.Formatting.Indented));
        try { Directory.Delete(staging, true); } catch { }
        Console.WriteLine($"\n  export: {exportedCount} glb written from {total} mesh ref(s)");
        Console.WriteLine($"  manifest: {Path.Combine(outDir, "_parts.json")}");
        break;
    }

    default:
        Console.WriteLine("unknown command: " + args[0]);
        break;
}

static Newtonsoft.Json.Linq.JToken ToJToken(object? value, int depth = 0)
{
    if (value == null) return Newtonsoft.Json.Linq.JValue.CreateNull();
    if (depth > 8) return new Newtonsoft.Json.Linq.JValue(value.ToString());
    var type = value.GetType();
    var typeName = type.Name;
    if (value is string s) return new Newtonsoft.Json.Linq.JValue(s);
    if (value is bool b) return new Newtonsoft.Json.Linq.JValue(b);
    if (type.IsPrimitive || value is decimal) return new Newtonsoft.Json.Linq.JValue(value);
    if (typeName == "FName")
    {
        var textProp = type.GetProperty("Text");
        return new Newtonsoft.Json.Linq.JValue(textProp != null ? textProp.GetValue(value)?.ToString() : value.ToString());
    }
    if (typeName == "FString" || typeName == "FText")
        return new Newtonsoft.Json.Linq.JValue(value.ToString());
    // property wrappers / arrays expose a Value member
    var valueProp = type.GetProperty("Value");
    if (valueProp != null)
    {
        var inner = valueProp.GetValue(value);
        if (ReferenceEquals(inner, value)) return new Newtonsoft.Json.Linq.JValue(value.ToString());
        return ToJToken(inner, depth + 1);
    }
    if (value is System.Collections.IEnumerable enumerable)
    {
        var arr = new Newtonsoft.Json.Linq.JArray();
        foreach (var item in enumerable) arr.Add(ToJToken(item, depth + 1));
        return arr;
    }
    return new Newtonsoft.Json.Linq.JValue(value.ToString());
}


