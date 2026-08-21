using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace GraphicsSettingsMigrator;

internal static class ExtendedDiscovery
{
    public static readonly string[] SupportedProducts =
    [
        "Adobe Lightroom Classic", "Affinity Designer", "Affinity Photo", "Affinity Publisher", "Aseprite",
        "Autodesk 3ds Max", "Autodesk Maya", "Blender", "Capture One",
        "Cinema 4D", "Clip Studio Paint", "CLO", "Corel Painter", "CorelDRAW",
        "GIMP", "Godot", "Houdini", "Inkscape", "KeyShot", "Krita",
        "Mari", "Marmoset Toolbag", "Marvelous Designer", "Modo", "Nuke",
        "paint.net", "PureRef", "Rhino", "SketchUp", "Unity", "Unreal Engine"
    ];

    public static void AddExisting(List<SettingsLocation> result, Func<string, string> portable)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        AddBlender(result, portable, roaming);
        AddMaya(result, portable, documents);
        Add3dsMax(result, portable, local);
        AddCinema4D(result, portable, roaming);
        AddHoudini(result, portable, profile, documents);
        AddRhino(result, portable, roaming);
        AddSketchUp(result, portable, roaming);
        AddFoundry(result, portable, profile, roaming);
        AddUnrealAndUnity(result, portable, roaming, local);
        AddGodot(result, portable, roaming);
        AddKrita(result, portable, roaming, local);
        AddGimpAndInkscape(result, portable, roaming);
        AddAffinity(result, portable, roaming, profile);
        AddCorelAndClipStudio(result, portable, roaming, documents);
        AddOtherCreativeApps(result, portable, roaming, local, documents);
    }

    public static void AddTargets(List<TargetLocation> targets)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        foreach (var install in SafeDirectories(Path.Combine(programFiles, "Blender Foundation"), "Blender *"))
        {
            var version = LastVersion(install.Name);
            if (version.Length > 0)
                AddTarget(targets, "blender", "Blender", version, "User profile",
                    Path.Combine(roaming, "Blender Foundation", "Blender", version));
        }

        foreach (var install in SafeDirectories(Path.Combine(programFiles, "Autodesk"), "Maya*"))
        {
            var version = LastVersion(install.Name);
            if (version.Length > 0)
                AddTarget(targets, "maya", "Autodesk Maya", version, "Preferences",
                    Path.Combine(MayaRoot(documents), version, "prefs"));
        }

        foreach (var install in SafeDirectories(Path.Combine(programFiles, "Autodesk"), "3ds Max *"))
        {
            var version = LastVersion(install.Name);
            if (version.Length > 0)
                AddTarget(targets, "3dsmax", "Autodesk 3ds Max", version, "User settings",
                    Path.Combine(local, "Autodesk", "3dsMax", version + " - 64bit", "ENU"));
        }

        foreach (var install in SafeDirectories(Path.Combine(programFiles, "Side Effects Software"), "Houdini *"))
        {
            var version = FirstMajorMinor(install.Name);
            if (version.Length > 0)
            {
                AddTarget(targets, "houdini", "Houdini", version, "User preferences",
                    Path.Combine(profile, "houdini" + version));
                AddTarget(targets, "houdini", "Houdini", version, "User preferences",
                    Path.Combine(documents, "houdini" + version));
            }
        }

        foreach (var install in SafeDirectories(programFiles, "Rhino *"))
        {
            var major = LastVersion(install.Name).Split('.')[0];
            if (int.TryParse(major, out _))
                AddTarget(targets, "rhino", "Rhino", major + ".0", "Settings",
                    Path.Combine(roaming, "McNeel", "Rhinoceros", major + ".0", "settings"));
        }

        foreach (var install in SafeDirectories(programFiles, "SketchUp *"))
        {
            var version = LastVersion(install.Name);
            AddTarget(targets, "sketchup", "SketchUp", version, "User data",
                Path.Combine(roaming, "SketchUp", install.Name, "SketchUp"));
        }

        AddTarget(targets, "nuke", "Nuke", "shared", "User profile", Path.Combine(profile, ".nuke"));
        AddTarget(targets, "mari", "Mari", "shared", "User profile", Path.Combine(profile, ".mari"));
        AddTarget(targets, "godot", "Godot", "shared", "Editor settings", Path.Combine(roaming, "Godot"));
        AddTarget(targets, "inkscape", "Inkscape", "shared", "User profile",
            Environment.GetEnvironmentVariable("INKSCAPE_PROFILE_DIR") ?? Path.Combine(roaming, "inkscape"));
        AddTarget(targets, "krita", "Krita", "shared", "Resources", Path.Combine(roaming, "krita"));
        AddTarget(targets, "krita", "Krita", "shared", "Core settings", Path.Combine(local, "kritarc"),
            SourceKind.File);
        AddTarget(targets, "gimp", "GIMP", "shared", "User profile", Path.Combine(roaming, "GIMP"));

        foreach (var app in new[] { "Photo", "Designer", "Publisher" })
        {
            AddTarget(targets, "affinity-" + app.ToLowerInvariant(), "Affinity " + app, "2", "User data",
                Path.Combine(roaming, "Affinity", app, "2.0"));
            AddTarget(targets, "affinity-" + app.ToLowerInvariant(), "Affinity " + app, "2", "User data",
                Path.Combine(profile, ".affinity", app, "2.0"));
        }
    }

    private static void AddBlender(List<SettingsLocation> result, Func<string, string> portable, string roaming)
    {
        var root = Path.Combine(roaming, "Blender Foundation", "Blender");
        foreach (var version in SafeDirectories(root, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+\.\d+")))
            AddDirectory(result, portable, "blender", "Blender", version.Name, "User profile",
                version.FullName, true,
                "Preferences, startup file, extensions, add-ons, scripts, and presets.",
                ["cache", "temp"]);
    }

    private static void AddMaya(List<SettingsLocation> result, Func<string, string> portable, string documents)
    {
        var root = MayaRoot(documents);
        foreach (var version in SafeDirectories(root, "*").Where(x =>
                     Regex.IsMatch(x.Name, @"^\d{4}(-x64)?$", RegexOptions.IgnoreCase)))
        {
            AddDirectory(result, portable, "maya", "Autodesk Maya", version.Name, "Preferences",
                Path.Combine(version.FullName, "prefs"), true,
                "Preferences, workspaces, shelves, hotkeys, colors, and marking menus.");
        }
        AddDirectory(result, portable, "maya", "Autodesk Maya", "shared", "Scripts",
            Path.Combine(root, "scripts"), true, "Shared user MEL and Python scripts.");
        AddDirectory(result, portable, "maya", "Autodesk Maya", "shared", "Render presets",
            Path.Combine(root, "Presets"), true, "User render setup presets.");
    }

    private static void Add3dsMax(List<SettingsLocation> result, Func<string, string> portable, string local)
    {
        var root = Path.Combine(local, "Autodesk", "3dsMax");
        foreach (var version in SafeDirectories(root, "* - 64bit"))
        foreach (var locale in SafeDirectories(version.FullName, "*"))
            AddDirectory(result, portable, "3dsmax", "Autodesk 3ds Max",
                Regex.Match(version.Name, @"^\d+").Value, "User settings", locale.FullName, true,
                "INI files, UI, hotkeys, macros, scripts, plug-in paths, and render presets.",
                ["temp", "autoback", "downloads"]);
    }

    private static void AddCinema4D(List<SettingsLocation> result, Func<string, string> portable, string roaming)
    {
        var maxon = Path.Combine(roaming, "Maxon");
        foreach (var profile in SafeDirectories(maxon, "Maxon Cinema 4D *"))
        {
            var version = Regex.Match(profile.Name, @"Cinema 4D (?<v>[^_]+)").Groups["v"].Value;
            AddDirectory(result, portable, "cinema4d", "Cinema 4D", version, "Preferences",
                Path.Combine(profile.FullName, "prefs"), true, "Application preferences and layouts.");
            AddDirectory(result, portable, "cinema4d", "Cinema 4D", version, "User plug-ins",
                Path.Combine(profile.FullName, "plugins"), true, "User-installed plug-ins.");
            AddDirectory(result, portable, "cinema4d", "Cinema 4D", version, "User library",
                Path.Combine(profile.FullName, "library"), false, "User libraries and assets.");
        }
    }

    private static void AddHoudini(List<SettingsLocation> result, Func<string, string> portable,
        string profile, string documents)
    {
        foreach (var root in new[] { profile, documents }.Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var dir in SafeDirectories(root, "houdini*").Where(x =>
                     Regex.IsMatch(x.Name, @"^houdini\d+\.\d+$", RegexOptions.IgnoreCase)))
        {
            var version = Regex.Match(dir.Name, @"\d+\.\d+").Value;
            AddDirectory(result, portable, "houdini", "Houdini", version, "User preferences",
                dir.FullName, true,
                "Preferences, hotkeys, desktops, shelves, packages, presets, scripts, and HDAs.",
                ["temp", "cache", "crash", "backup"]);
        }
    }

    private static void AddRhino(List<SettingsLocation> result, Func<string, string> portable, string roaming)
    {
        var root = Path.Combine(roaming, "McNeel", "Rhinoceros");
        foreach (var version in SafeDirectories(root, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+\.\d+$")))
            AddDirectory(result, portable, "rhino", "Rhino", version.Name, "Settings",
                Path.Combine(version.FullName, "settings"), true,
                "Application and command settings, including named schemes.");
    }

    private static void AddSketchUp(List<SettingsLocation> result, Func<string, string> portable, string roaming)
    {
        var root = Path.Combine(roaming, "SketchUp");
        foreach (var version in SafeDirectories(root, "SketchUp *"))
            AddDirectory(result, portable, "sketchup", "SketchUp", LastVersion(version.Name), "User data",
                Path.Combine(version.FullName, "SketchUp"), true,
                "Extensions, templates, materials, styles, and user files.", ["WebCache", "Logs"]);
    }

    private static void AddFoundry(List<SettingsLocation> result, Func<string, string> portable,
        string profile, string roaming)
    {
        AddDirectory(result, portable, "nuke", "Nuke", "shared", "User profile",
            Path.Combine(profile, ".nuke"), true, "Preferences, menus, toolsets, gizmos, and Python scripts.",
            ["Cache", "crash"]);
        AddDirectory(result, portable, "mari", "Mari", "shared", "User profile",
            Path.Combine(profile, ".mari"), true, "Preferences, shelves, scripts, and user tools.",
            ["Cache", "Logs"]);
        AddDirectory(result, portable, "modo", "Modo", "shared", "User data",
            Path.Combine(roaming, "Luxology"), true, "Configs, scripts, presets, and kits.",
            ["Temp", "Cache"]);
        AddDirectory(result, portable, "modo", "Modo", "shared", "User data",
            Path.Combine(roaming, "Foundry", "Modo"), true, "Configs, scripts, presets, and kits.",
            ["Temp", "Cache"]);
    }

    private static void AddUnrealAndUnity(List<SettingsLocation> result, Func<string, string> portable,
        string roaming, string local)
    {
        var unrealRoot = Path.Combine(local, "UnrealEngine");
        foreach (var version in SafeDirectories(unrealRoot, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+\.\d+")))
        {
            var saved = Path.Combine(version.FullName, "Saved");
            foreach (var config in new[] { "Config", "Config\\WindowsEditor", "Config\\Windows" })
                if (Directory.Exists(Path.Combine(saved, config)))
                {
                    AddDirectory(result, portable, "unreal", "Unreal Engine", version.Name,
                        "Global editor config", Path.Combine(saved, config), true,
                        "Global editor settings. Project-specific settings remain inside each project.");
                    break;
                }
        }

        AddDirectory(result, portable, "unity", "Unity", "shared", "Editor preferences",
            Path.Combine(roaming, "Unity", "Editor-5.x", "Preferences"), true,
            "Editor layouts and file-based preferences.", ["Cache", "Logs"]);
    }

    private static void AddGodot(List<SettingsLocation> result, Func<string, string> portable, string roaming)
    {
        AddDirectory(result, portable, "godot", "Godot", "shared", "Editor settings",
            Path.Combine(roaming, "Godot"), true, "Editor settings, layouts, script templates, and feature profiles.",
            ["cache", "shader_cache", "logs"]);
    }

    private static void AddKrita(List<SettingsLocation> result, Func<string, string> portable,
        string roaming, string local)
    {
        AddFile(result, portable, "krita", "Krita", "shared", "Core settings",
            Path.Combine(local, "kritarc"), true, "Main Krita preferences.");
        AddFile(result, portable, "krita", "Krita", "shared", "Keyboard shortcuts",
            Path.Combine(local, "kritashortcutsrc"), true, "Custom keyboard shortcuts.");
        AddFile(result, portable, "krita", "Krita", "shared", "Display settings",
            Path.Combine(local, "kritadisplayrc"), true, "Display configuration.");
        AddDirectory(result, portable, "krita", "Krita", "shared", "Resources",
            Path.Combine(roaming, "krita"), true, "Brushes, presets, bundles, patterns, gradients, and resource tags.",
            ["cache"]);

        var packages = Path.Combine(local, "Packages");
        foreach (var package in SafeDirectories(packages, "49800Krita_*"))
        {
            AddFile(result, portable, "krita-store", "Krita (Microsoft Store)", "shared", "Core settings",
                Path.Combine(package.FullName, "LocalCache", "Local", "kritarc"), true, "Main Krita preferences.");
            AddDirectory(result, portable, "krita-store", "Krita (Microsoft Store)", "shared", "Resources",
                Path.Combine(package.FullName, "LocalCache", "Roaming", "krita"), true, "Custom Krita resources.");
        }
    }

    private static void AddGimpAndInkscape(List<SettingsLocation> result, Func<string, string> portable,
        string roaming)
    {
        var gimp = Path.Combine(roaming, "GIMP");
        foreach (var version in SafeDirectories(gimp, "*"))
            AddDirectory(result, portable, "gimp", "GIMP", version.Name, "User profile", version.FullName, true,
                "Preferences, shortcuts, brushes, dynamics, gradients, palettes, plug-ins, and scripts.",
                ["cache", "tmp"]);

        var inkscape = Environment.GetEnvironmentVariable("INKSCAPE_PROFILE_DIR")
                       ?? Path.Combine(roaming, "inkscape");
        AddDirectory(result, portable, "inkscape", "Inkscape", "shared", "User profile", inkscape, true,
            "preferences.xml, extensions, templates, palettes, keys, icons, and filters.",
            ["cache"]);
    }

    private static void AddAffinity(List<SettingsLocation> result, Func<string, string> portable,
        string roaming, string profile)
    {
        foreach (var app in new[] { "Photo", "Designer", "Publisher" })
        {
            var appId = "affinity-" + app.ToLowerInvariant();
            AddDirectory(result, portable, appId, "Affinity " + app, "2", "User data",
                Path.Combine(roaming, "Affinity", app, "2.0"), true,
                "Preferences, UI, shortcuts, brushes, assets, macros, and presets.", ["CrashReports", "logs"]);
            AddDirectory(result, portable, appId, "Affinity " + app, "2", "User data",
                Path.Combine(profile, ".affinity", app, "2.0"), true,
                "Preferences and resources for the MSIX installation.", ["CrashReports", "logs"]);
        }
    }

    private static void AddCorelAndClipStudio(List<SettingsLocation> result, Func<string, string> portable,
        string roaming, string documents)
    {
        var corel = Path.Combine(roaming, "Corel");
        foreach (var dir in SafeDirectories(corel, "CorelDRAW Graphics Suite *"))
            AddDirectory(result, portable, "coreldraw", "CorelDRAW", LastVersion(dir.Name), "User profile",
                dir.FullName, true, "Workspaces, shortcuts, presets, and application preferences.",
                ["Messages", "Logs", "Cache"]);
        foreach (var dir in SafeDirectories(corel, "Painter*"))
            AddDirectory(result, portable, "corel-painter", "Corel Painter", LastVersion(dir.Name), "User profile",
                dir.FullName, true, "Preferences, workspaces, brushes, and presets.",
                ["Logs", "Cache"]);

        AddDirectory(result, portable, "clip-studio", "Clip Studio Paint", "shared", "Application data",
            Path.Combine(roaming, "CELSYS"), true, "Preferences, shortcuts, workspaces, and application data.",
            ["CLIPStudioCommon\\Material\\Document", "Temp", "Cache"]);
        AddDirectory(result, portable, "clip-studio", "Clip Studio Paint", "shared", "User materials",
            Path.Combine(documents, "CELSYS"), false, "Downloaded and custom materials; may be large.",
            ["Temp", "Cache"]);
    }

    private static void AddOtherCreativeApps(List<SettingsLocation> result, Func<string, string> portable,
        string roaming, string local, string documents)
    {
        foreach (var dir in SafeDirectories(roaming, "Marmoset Toolbag*"))
            AddDirectory(result, portable, "marmoset", "Marmoset Toolbag", LastVersion(dir.Name), "User settings",
                dir.FullName, true, "Preferences, layouts, presets, and user data.", ["Cache", "Logs"]);
        foreach (var vendor in new[] { Path.Combine(roaming, "CLO Virtual Fashion"), Path.Combine(roaming, "CLO") })
            foreach (var dir in SafeDirectories(vendor, "*"))
                if (dir.Name.Contains("Marvelous", StringComparison.OrdinalIgnoreCase) ||
                    dir.Name.Equals("CLO", StringComparison.OrdinalIgnoreCase))
                    AddDirectory(result, portable, "clo-md", dir.Name, LastVersion(dir.Name), "User settings",
                        dir.FullName, true, "Preferences, shortcuts, UI layouts, and presets.", ["Cache", "Logs"]);

        foreach (var dir in SafeDirectories(documents, "KeyShot*"))
            AddDirectory(result, portable, "keyshot", "KeyShot", LastVersion(dir.Name), "User resources",
                dir.FullName, false, "Materials, environments, textures, templates, and presets; may be large.",
                ["Scenes", "Renderings", "Backplates"]);

        AddDirectory(result, portable, "paintdotnet", "paint.net", "shared", "User data",
            Path.Combine(local, "paint.net"), true, "Settings, custom shapes, effects, and user data.",
            ["CrashLogs", "Updates"]);
        AddDirectory(result, portable, "aseprite", "Aseprite", "shared", "User data",
            Path.Combine(roaming, "Aseprite"), true, "Preferences, keyboard shortcuts, scripts, extensions, and palettes.",
            ["crash", "cache"]);
        AddDirectory(result, portable, "pureref", "PureRef", "shared", "Settings",
            Path.Combine(roaming, "PureRef"), true, "Application preferences and shortcuts.", ["Cache", "Logs"]);
        AddDirectory(result, portable, "capture-one", "Capture One", "shared", "User settings",
            Path.Combine(local, "CaptureOne"), true, "Workspaces, shortcuts, styles, presets, and preferences.",
            ["Cache", "Logs", "CrashReports"]);
        AddDirectory(result, portable, "lightroom", "Adobe Lightroom Classic", "shared", "User settings",
            Path.Combine(roaming, "Adobe", "Lightroom"), true,
            "Develop presets, metadata presets, templates, preferences, and modules.",
            ["Caches", "Logs"]);
    }

    private static string MayaRoot(string documents) =>
        Environment.GetEnvironmentVariable("MAYA_APP_DIR") ?? Path.Combine(documents, "maya");

    private static void AddDirectory(List<SettingsLocation> result, Func<string, string> portable,
        string appId, string product, string version, string category, string path, bool recommended,
        string notes, IEnumerable<string>? exclusions = null)
    {
        if (!Directory.Exists(path)) return;
        var excluded = exclusions?.ToList() ?? [];
        var (count, bytes) = Measure(path, excluded);
        result.Add(new SettingsLocation
        {
            AppId = appId,
            Product = product,
            Version = string.IsNullOrWhiteSpace(version) ? "shared" : version,
            Category = category,
            Kind = SourceKind.Directory,
            SourcePath = path,
            PortablePath = portable(path),
            Recommended = recommended,
            Notes = notes,
            FileCount = count,
            SizeBytes = bytes,
            ExcludedPrefixes = excluded
        });
    }

    private static void AddFile(List<SettingsLocation> result, Func<string, string> portable,
        string appId, string product, string version, string category, string path, bool recommended, string notes)
    {
        if (!File.Exists(path)) return;
        var info = new FileInfo(path);
        result.Add(new SettingsLocation
        {
            AppId = appId,
            Product = product,
            Version = version,
            Category = category,
            Kind = SourceKind.File,
            SourcePath = path,
            PortablePath = portable(path),
            Recommended = recommended,
            Notes = notes,
            FileCount = 1,
            SizeBytes = info.Length
        });
    }

    private static (int Count, long Bytes) Measure(string root, IReadOnlyCollection<string> exclusions)
    {
        var count = 0;
        long bytes = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path);
                if (DiscoveryService.IsExcluded(relative, exclusions)) continue;
                try
                {
                    count++;
                    bytes += new FileInfo(path).Length;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes);
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(string root, string pattern)
    {
        try
        {
            return Directory.Exists(root)
                ? new DirectoryInfo(root).EnumerateDirectories(pattern, SearchOption.TopDirectoryOnly).ToArray()
                : [];
        }
        catch { return []; }
    }

    private static void AddTarget(List<TargetLocation> targets, string appId, string product, string version,
        string category, string path, SourceKind kind = SourceKind.Directory)
    {
        targets.Add(new TargetLocation
        {
            AppId = appId,
            Product = product,
            Version = version,
            Category = category,
            Kind = kind,
            TargetPath = path,
            Exists = kind == SourceKind.File ? File.Exists(path) : Directory.Exists(path)
        });
    }

    private static string LastVersion(string text)
    {
        var matches = Regex.Matches(text, @"\d+(?:\.\d+)*");
        return matches.Count == 0 ? "" : matches[^1].Value;
    }

    private static string FirstMajorMinor(string text) =>
        Regex.Match(text, @"\d+\.\d+").Value;
}
