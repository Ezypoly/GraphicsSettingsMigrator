using Microsoft.Win32;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GraphicsSettingsMigrator;

public sealed class DiscoveryService
{
    private readonly string _roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private readonly string _local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private readonly string _profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private readonly string _publicDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
    private readonly string _programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private readonly string _programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private readonly string _programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private readonly string _commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
    private readonly string _commonProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);

    public List<SettingsLocation> DiscoverExisting()
    {
        var result = new List<SettingsLocation>();
        DiscoverAdobe(result);
        DiscoverZBrush(result);
        Discover3DCoat(result);
        DiscoverPlasticity(result);
        ExtendedDiscovery.AddExisting(result, MakePortablePath);
        CustomContentDiscovery.AddExisting(result, MakePortablePath);
        return result
            .GroupBy(x => x.Kind + "|" + x.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Product, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(x => VersionKey(x.Version))
            .ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public List<TargetLocation> DiscoverTargets()
    {
        var targets = DiscoverExisting().Select(x => new TargetLocation
        {
            AppId = x.AppId,
            Product = x.Product,
            Version = x.Version,
            Category = x.Category,
            Kind = x.Kind,
            TargetPath = x.SourcePath,
            Exists = true
        }).ToList();

        var adobePrograms = Path.Combine(_programFiles, "Adobe");
        foreach (var dir in SafeDirectories(adobePrograms, "Adobe Photoshop *"))
        {
            var version = dir.Name["Adobe Photoshop ".Length..];
            AddTarget(targets, "photoshop", "Adobe Photoshop", version, "Core settings",
                Path.Combine(_roaming, "Adobe", dir.Name, dir.Name + " Settings"));
            AddTarget(targets, "photoshop", "Adobe Photoshop", version, "Presets",
                Path.Combine(_roaming, "Adobe", dir.Name, "Presets"));
        }

        foreach (var dir in SafeDirectories(adobePrograms, "Adobe Illustrator *"))
        {
            var yearText = dir.Name["Adobe Illustrator ".Length..];
            if (int.TryParse(Regex.Match(yearText, @"\d{4}").Value, out var year))
            {
                var major = year - 1996;
                AddTarget(targets, "illustrator", "Adobe Illustrator", yearText, "Settings",
                    Path.Combine(_roaming, "Adobe", "Adobe Illustrator " + major + " Settings", "en_US", "x64"));
            }
        }

        foreach (var dir in SafeDirectories(adobePrograms, "Adobe After Effects *"))
        {
            var yearText = dir.Name["Adobe After Effects ".Length..];
            var version = AdobeVideoVersion(yearText);
            AddTarget(targets, "aftereffects", "Adobe After Effects", version, "Settings",
                Path.Combine(_roaming, "Adobe", "After Effects", version));
        }

        foreach (var dir in SafeDirectories(adobePrograms, "Adobe Media Encoder *"))
        {
            var yearText = dir.Name["Adobe Media Encoder ".Length..];
            var version = AdobeVideoVersion(yearText);
            AddTarget(targets, "mediaencoder", "Adobe Media Encoder", version, "Settings",
                Path.Combine(_roaming, "Adobe", "Adobe Media Encoder", version));
        }

        foreach (var dir in SafeDirectories(_programFiles, "Maxon ZBrush *"))
        {
            var version = dir.Name["Maxon ZBrush ".Length..];
            var root = Path.Combine(_publicDocuments, "ZBrushData" + version);
            AddTarget(targets, "zbrush", "Maxon ZBrush", version, "UI, hotkeys and macros",
                Path.Combine(root, "ZStartup"));
            AddTarget(targets, "zbrush", "Maxon ZBrush", version, "Plugin settings",
                Path.Combine(root, "ZPluginData"));
            AddTarget(targets, "zbrush", "Maxon ZBrush", version, "Preferences",
                Path.Combine(root, "Preferences"));
        }

        AddTarget(targets, "3dcoat", "3DCoat", "shared", "UserPrefs",
            Path.Combine(_documents, "3DCoat", "UserPrefs"));
        AddTarget(targets, "plasticity", "Plasticity", "shared", "JSON settings",
            Path.Combine(_profile, ".plasticity"));

        ExtendedDiscovery.AddTargets(targets);
        CustomContentDiscovery.AddTargets(targets);
        AddTarget(targets, "3dcoat", "3DCoat", "shared", "Option presets",
            Path.Combine(_documents, "3DCoat", "data", "OptionsPresets"));
        AddTarget(targets, "3dcoat", "3DCoat", "shared", "Tool presets",
            Path.Combine(_documents, "3DCoat", "data", "ToolsPresets"));

        return targets
            .GroupBy(x => x.Kind + "|" + x.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    public List<TargetLocation> FindTargetCandidates(BackupEntry entry)
    {
        var candidates = DiscoverTargets()
            .Where(x => x.AppId == entry.AppId && x.Category == entry.Category && x.Kind == entry.Kind)
            .ToList();

        var samePath = ExpandPortablePath(entry.PortablePath);
        if (!string.IsNullOrWhiteSpace(samePath))
        {
            candidates.Add(new TargetLocation
            {
                AppId = entry.AppId,
                Product = entry.Product,
                Version = entry.SourceVersion,
                Category = entry.Category,
                Kind = entry.Kind,
                TargetPath = samePath,
                Exists = entry.Kind == SourceKind.Registry
                    ? RegistryPathExists(samePath)
                    : Directory.Exists(samePath) || File.Exists(samePath)
            });
        }

        return candidates
            .GroupBy(x => x.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.Exists).First())
            .OrderByDescending(x => VersionKey(x.Version))
            .ThenByDescending(x => x.Exists)
            .ToList();
    }

    public string MakePortablePath(string path)
    {
        if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return path;

        var roots = new[]
        {
            (_publicDocuments, "%PUBLIC_DOCUMENTS%"),
            (_documents, "%DOCUMENTS%"),
            (_roaming, "%APPDATA%"),
            (_local, "%LOCALAPPDATA%"),
            (_profile, "%USERPROFILE%"),
            (_commonProgramFilesX86, "%COMMONPROGRAMFILES_X86%"),
            (_commonProgramFiles, "%COMMONPROGRAMFILES%"),
            (_programFilesX86, "%PROGRAMFILES_X86%"),
            (_programFiles, "%PROGRAMFILES%"),
            (_programData, "%PROGRAMDATA%")
        };

        foreach (var (root, token) in roots.Where(x => !string.IsNullOrWhiteSpace(x.Item1))
                     .OrderByDescending(x => x.Item1.Length))
        {
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
                return token;
            if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return token + path[root.Length..];
        }

        return path;
    }

    public string ExpandPortablePath(string path)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%PUBLIC_DOCUMENTS%"] = _publicDocuments,
            ["%DOCUMENTS%"] = _documents,
            ["%APPDATA%"] = _roaming,
            ["%LOCALAPPDATA%"] = _local,
            ["%USERPROFILE%"] = _profile,
            ["%COMMONPROGRAMFILES_X86%"] = _commonProgramFilesX86,
            ["%COMMONPROGRAMFILES%"] = _commonProgramFiles,
            ["%PROGRAMFILES_X86%"] = _programFilesX86,
            ["%PROGRAMFILES%"] = _programFiles,
            ["%PROGRAMDATA%"] = _programData
        };

        foreach (var pair in replacements)
        {
            if (path.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                return pair.Value + path[pair.Key.Length..];
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private void DiscoverAdobe(List<SettingsLocation> result)
    {
        var adobeRoaming = Path.Combine(_roaming, "Adobe");
        var adobeLocal = Path.Combine(_local, "Adobe");

        foreach (var productDir in SafeDirectories(adobeRoaming, "Adobe Photoshop *"))
        {
            var version = productDir.Name["Adobe Photoshop ".Length..];
            var settings = Path.Combine(productDir.FullName, productDir.Name + " Settings");
            AddDirectory(result, "photoshop", "Adobe Photoshop", version, "Core settings", settings, true,
                "Actions Palette, workspaces, hotkeys, brushes, and core preferences.");
            AddDirectory(result, "photoshop", "Adobe Photoshop", version, "Presets",
                Path.Combine(productDir.FullName, "Presets"), true,
                "Exported Actions, keyboard shortcut sets, and user presets.");
        }

        foreach (var settingsDir in SafeDirectories(adobeRoaming, "Adobe Illustrator * Settings"))
        {
            var match = Regex.Match(settingsDir.Name, @"Adobe Illustrator (?<v>[\d.]+) Settings",
                RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            foreach (var locale in SafeDirectories(settingsDir.FullName, "*"))
            foreach (var arch in SafeDirectories(locale.FullName, "x*"))
                AddDirectory(result, "illustrator", "Adobe Illustrator", match.Groups["v"].Value,
                    "Settings", arch.FullName, true, "Preferences, workspaces, and hotkeys.");
        }

        var aeRoot = Path.Combine(adobeRoaming, "After Effects");
        foreach (var dir in SafeDirectories(aeRoot, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+(\.\d+)*$")))
            AddDirectory(result, "aftereffects", "Adobe After Effects", dir.Name, "Settings", dir.FullName,
                true, "Preferences, modified workspaces, and keyboard shortcuts.");

        var ameRoot = Path.Combine(adobeRoaming, "Adobe Media Encoder");
        foreach (var dir in SafeDirectories(ameRoot, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+(\.\d+)*$")))
            AddDirectory(result, "mediaencoder", "Adobe Media Encoder", dir.Name, "Settings", dir.FullName,
                true, "Queue and application settings.");

        AddDirectory(result, "cameraraw", "Adobe Camera Raw", "shared", "Profiles and presets",
            Path.Combine(adobeRoaming, "CameraRaw"), true,
            "Profiles, defaults, and XMP presets shared by Photoshop and other Adobe applications.");

        AddRegistry(result, "substance-painter", "Adobe Substance 3D Painter", "shared", "Registry settings",
            @"HKCU\Software\Adobe\Adobe Substance 3D Painter", true,
            "Hotkeys, Shelf/Asset paths, and UI layout.");
        AddDirectory(result, "substance-painter", "Adobe Substance 3D Painter", "shared", "Roaming data",
            Path.Combine(adobeRoaming, "Adobe Substance 3D Painter"), true, "Painter user data.");
        AddDirectory(result, "substance-painter", "Adobe Substance 3D Painter", "shared", "Local data",
            Path.Combine(adobeLocal, "Adobe Substance 3D Painter"), false,
            "Additional data; may include caches.", ["Crashpad", "cache", "logs"]);

        AddDirectory(result, "substance-designer", "Adobe Substance 3D Designer", "shared", "Settings",
            Path.Combine(adobeLocal, "Adobe Substance 3D Designer"), true,
            "user_preferences.xml, configuration projects, and user tags.",
            ["Crashpad", "cache", "log.txt"]);
        AddRegistry(result, "substance-designer", "Adobe Substance 3D Designer", "shared", "Registry settings",
            @"HKCU\Software\Adobe\Adobe Substance 3D Designer", false, "Additional Designer settings.");

        AddDirectory(result, "substance-modeler", "Adobe Substance 3D Modeler", "shared", "Settings",
            Path.Combine(adobeLocal, "Adobe Substance 3D Modeler", "pref"), true,
            "UI, tool, and session state.");

        AddRegistry(result, "substance-sampler", "Adobe Substance 3D Sampler", "shared", "Registry settings",
            @"HKCU\Software\Adobe\Adobe Substance 3D Sampler", true, "Core preferences.");
        AddDirectory(result, "substance-sampler", "Adobe Substance 3D Sampler", "shared", "Roaming data",
            Path.Combine(adobeRoaming, "Adobe Substance 3D Sampler"), true,
            "Scripts, plugins, and user materials.",
            ["renderCache", "thumbnailCache", "Logs"]);
        AddDirectory(result, "substance-sampler", "Adobe Substance 3D Sampler", "shared", "Local data",
            Path.Combine(adobeLocal, "Adobe Substance 3D Sampler"), false,
            "Caches and auxiliary data.", ["Crashpad", "cache", "logs"]);
    }

    private void DiscoverZBrush(List<SettingsLocation> result)
    {
        foreach (var root in SafeDirectories(_publicDocuments, "ZBrushData*"))
        {
            var version = root.Name["ZBrushData".Length..];
            AddDirectory(result, "zbrush", "Maxon ZBrush", version,
                "UI, hotkeys and macros", Path.Combine(root.FullName, "ZStartup"), true,
                "CustomUserInterface, StartupHotkeys, ConfigFiles, Macros, and StartupDocument.");
            AddDirectory(result, "zbrush", "Maxon ZBrush", version,
                "Plugin settings", Path.Combine(root.FullName, "ZPluginData"), true,
                "Small plugin-specific user data.");
            AddDirectory(result, "zbrush", "Maxon ZBrush", version,
                "Preferences", Path.Combine(root.FullName, "Preferences"), true,
                "Additional preferences and saved application state.");
        }
    }

    private void Discover3DCoat(List<SettingsLocation> result)
    {
        var coatRoot = Environment.GetEnvironmentVariable("COAT_USER_PATH")
            ?? Environment.GetEnvironmentVariable("COAT_FILES_PATH")
            ?? Path.Combine(_documents, "3DCoat");
        AddDirectory(result, "3dcoat", "3DCoat", "shared", "UserPrefs",
            Path.Combine(coatRoot, "UserPrefs"), true,
            "Preferences, hotkeys, UI, brushes, materials, presets, and custom tools.",
            ["UserScenes"]);
        AddDirectory(result, "3dcoat", "3DCoat", "shared", "Option presets",
            Path.Combine(coatRoot, "data", "OptionsPresets"), true,
            "Additional option presets stored outside UserPrefs.");
        AddDirectory(result, "3dcoat", "3DCoat", "shared", "Tool presets",
            Path.Combine(coatRoot, "data", "ToolsPresets"), true,
            "Additional tool presets stored outside UserPrefs.");
    }

    private void DiscoverPlasticity(List<SettingsLocation> result)
    {
        AddDirectory(result, "plasticity", "Plasticity", "shared", "JSON settings",
            Path.Combine(_profile, ".plasticity"), true, "settings.json, keymap.json, and theme.json.");
        AddDirectory(result, "plasticity", "Plasticity", "shared", "JSON settings",
            Path.Combine(_profile, ".config", "Plasticity"), true, "Path used by newer or alternative builds.");
        AddDirectory(result, "plasticity", "Plasticity", "shared", "Roaming data",
            Path.Combine(_roaming, "Plasticity"), false, "Electron application data; may include caches.",
            ["Cache", "Code Cache", "GPUCache", "Crashpad", "logs"]);
    }

    private void AddDirectory(List<SettingsLocation> result, string appId, string product, string version,
        string category, string path, bool recommended, string notes, IEnumerable<string>? exclusions = null)
    {
        if (!Directory.Exists(path)) return;
        var (count, bytes) = MeasureDirectory(path, exclusions);
        result.Add(new SettingsLocation
        {
            AppId = appId,
            Product = product,
            Version = version,
            Category = category,
            Kind = SourceKind.Directory,
            SourcePath = path,
            PortablePath = MakePortablePath(path),
            Recommended = recommended,
            Notes = notes,
            FileCount = count,
            SizeBytes = bytes,
            ExcludedPrefixes = exclusions?.ToList() ?? []
        });
    }

    private void AddRegistry(List<SettingsLocation> result, string appId, string product, string version,
        string category, string path, bool recommended, string notes)
    {
        if (!RegistryPathExists(path)) return;
        result.Add(new SettingsLocation
        {
            AppId = appId,
            Product = product,
            Version = version,
            Category = category,
            Kind = SourceKind.Registry,
            SourcePath = path,
            PortablePath = path,
            Recommended = recommended,
            Notes = notes
        });
    }

    private static (int Count, long Bytes) MeasureDirectory(string root, IEnumerable<string>? exclusions)
    {
        var count = 0;
        long bytes = 0;
        var excluded = exclusions?.ToArray() ?? [];
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (IsExcluded(relative, excluded)) continue;
                try
                {
                    var info = new FileInfo(file);
                    count++;
                    bytes += info.Length;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes);
    }

    public static bool IsExcluded(string relativePath, IEnumerable<string> prefixes)
    {
        var normalized = relativePath.Replace('/', '\\');
        foreach (var prefix in prefixes)
        {
            var p = prefix.Replace('/', '\\').Trim('\\');
            if (normalized.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(string root, string pattern)
    {
        try
        {
            if (!Directory.Exists(root)) return [];
            return new DirectoryInfo(root).EnumerateDirectories(pattern, SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            return [];
        }
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
            Exists = kind == SourceKind.Registry ? RegistryPathExists(path) :
                Directory.Exists(path) || File.Exists(path)
        });
    }

    private static bool RegistryPathExists(string path)
    {
        try
        {
            var subPath = path.Replace('/', '\\');
            if (subPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
                subPath = subPath[5..];
            using var key = Registry.CurrentUser.OpenSubKey(subPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    private static string AdobeVideoVersion(string yearText)
    {
        var match = Regex.Match(yearText, @"\d{4}");
        return int.TryParse(match.Value, out var year)
            ? (year - 2000).ToString(CultureInfo.InvariantCulture) + ".0"
            : yearText;
    }

    private static long VersionKey(string version)
    {
        var numbers = Regex.Matches(version ?? "", @"\d+")
            .Select(x => long.TryParse(x.Value, out var value) ? value : 0)
            .Take(3)
            .ToArray();
        long key = 0;
        foreach (var number in numbers)
            key = key * 10000 + Math.Min(number, 9999);
        return key;
    }
}
