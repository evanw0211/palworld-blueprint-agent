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

if (args.Length == 0)
{
    Console.WriteLine("usage:");
    Console.WriteLine("  list <substring> [--exclude <substring>] [--limit N]");
    Console.WriteLine("  info");
    return;
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
        Console.WriteLine($"  {packageNames.Count} build-object blueprints available in the pak");

        string? ResolveBp(string bp)
        {
            var bare = bp.StartsWith("BP_BuildObject_") ? bp["BP_BuildObject_".Length..] : bp;
            if (packageNames.Contains(bp)) return bp;
            if (aliases.TryGetValue(bare, out var alias) && packageNames.Contains("BP_BuildObject_" + alias))
                return "BP_BuildObject_" + alias;
            if (packageNames.Contains("BP_BuildObject_" + bare)) return "BP_BuildObject_" + bare;
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


