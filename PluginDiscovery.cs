using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GraphicsSettingsMigrator;

/// <summary>
/// Finds plug-in roots that live outside the application profiles handled by the main scanners.
/// Install-level locations are kept separate because they can be large, version-specific, and protected.
/// </summary>
internal static class PluginDiscovery
{
    private sealed record Candidate(string AppId, string Product, string Version, string Category,
        string Path, bool Recommended, string Notes, IReadOnlyCollection<string>? Exclusions = null);

    public static void AddExisting(List<SettingsLocation> result, Func<string, string> portable)
    {
        foreach (var candidate in Candidates()
                     .Where(x => Directory.Exists(x.Path))
                     .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.First()))
        {
            var exclusions = candidate.Exclusions?.ToList() ?? [];
            var (count, bytes) = Measure(candidate.Path, exclusions);
            result.Add(new SettingsLocation
            {
                AppId = candidate.AppId,
                Product = candidate.Product,
                Version = candidate.Version,
                Category = candidate.Category,
                Kind = SourceKind.Directory,
                SourcePath = candidate.Path,
                PortablePath = portable(candidate.Path),
                Recommended = candidate.Recommended,
                Notes = candidate.Notes,
                FileCount = count,
                SizeBytes = bytes,
                ExcludedPrefixes = exclusions
            });
        }
    }

    public static void AddTargets(List<TargetLocation> targets)
    {
        foreach (var candidate in Candidates().GroupBy(
                     x => x.AppId + "|" + x.Category + "|" + x.Path,
                     StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
        {
            targets.Add(new TargetLocation
            {
                AppId = candidate.AppId,
                Product = candidate.Product,
                Version = candidate.Version,
                Category = candidate.Category,
                Kind = SourceKind.Directory,
                TargetPath = candidate.Path,
                Exists = Directory.Exists(candidate.Path)
            });
        }
    }

    private static List<Candidate> Candidates()
    {
        var result = new List<Candidate>();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        AddZBrush(result, roaming);
        AddAutodesk(result, roaming, documents, programData);
        AddCinema4D(result);
        AddEnvironmentPaths(result, "houdini", "Houdini", "shared", "External plug-in path",
            "HOUDINI_PATH", true, "External Houdini assets and plug-ins configured through HOUDINI_PATH.");
        AddEnvironmentPaths(result, "houdini", "Houdini", "shared", "External package path",
            "HOUDINI_PACKAGE_DIR", true, "External Houdini package definitions and plug-in packages.");
        AddNukeAndOpenFx(result);
        AddPaintDotNet(result, documents);
        AddUnreal(result, programData);

        AddEnvironmentPaths(result, "blender", "Blender", "shared", "System scripts and add-ons",
            "BLENDER_SYSTEM_SCRIPTS", false,
            "System-wide Blender scripts and add-ons. Bundled components may be version-specific.");
        AddEnvironmentPaths(result, "mari", "Mari", "shared", "External scripts and plug-ins",
            "MARI_SCRIPT_PATH", true, "External Mari scripts and plug-ins from MARI_SCRIPT_PATH.");

        AddIfPath(result, "capture-one", "Capture One", "shared", "Plug-ins",
            Path.Combine(local, "CaptureOne", "Plugins"), true,
            "Capture One user plug-ins stored outside the normal settings folders.");
        return result;
    }

    private static void AddZBrush(List<Candidate> result, string roaming)
    {
        foreach (var install in ProgramRoots().SelectMany(root => SafeDirectories(root, "*ZBrush*"))
                     .Where(x => x.Name.Contains("ZBrush", StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
        {
            var version = LastVersion(install.Name);
            AddIfPath(result, "zbrush", "Maxon ZBrush", version, "Installed plug-ins",
                Path.Combine(install.FullName, "ZStartup", "ZPlugs64"), true,
                "Custom and installed ZBrush plug-ins. Native plug-ins are version-specific; administrator rights may be required to restore.");
            AddIfPath(result, "zbrush", "Maxon ZBrush", version, "ZData plug-ins",
                Path.Combine(install.FullName, "ZData", "ZPlugs64"), false,
                "ZBrush ZData plug-ins and shipped assets. Usually reinstallable, large, and version-specific.");
        }

        var maxon = Path.Combine(roaming, "Maxon");
        foreach (var profile in SafeDirectories(maxon, "ZBrush_*"))
            AddIfPath(result, "zbrush", "Maxon ZBrush", LastVersion(profile.Name), "User plug-ins",
                Path.Combine(profile.FullName, "ZStartup", "ZPlugs64"), true,
                "Per-user plug-ins used by newer ZBrush releases.");
    }

    private static void AddAutodesk(List<Candidate> result, string roaming, string documents,
        string programData)
    {
        AddIfPath(result, "autodesk-shared", "Autodesk shared plug-ins", "shared",
            "Application plug-ins (all users)", Path.Combine(programData, "Autodesk", "ApplicationPlugins"), true,
            "Autodesk Application Plug-in Packages available to every user. May be version-specific.");
        AddIfPath(result, "autodesk-shared", "Autodesk shared plug-ins", "shared",
            "Application plug-ins (user)", Path.Combine(roaming, "Autodesk", "ApplicationPlugins"), true,
            "Per-user Autodesk Application Plug-in Packages.");
        foreach (var root in ProgramRoots())
            AddIfPath(result, "autodesk-shared", "Autodesk shared plug-ins", "shared",
                "Application plug-ins (Program Files)", Path.Combine(root, "Autodesk", "ApplicationPlugins"), false,
                "Protected Autodesk Application Plug-in Packages. Administrator rights may be required to restore.");
        AddEnvironmentPaths(result, "autodesk-shared", "Autodesk shared plug-ins", "shared",
            "External ApplicationPlugins", "ADSK_APPLICATION_PLUGINS", true,
            "Additional Autodesk package roots configured through ADSK_APPLICATION_PLUGINS.");

        var mayaRoot = Environment.GetEnvironmentVariable("MAYA_APP_DIR") ?? Path.Combine(documents, "maya");
        foreach (var version in SafeDirectories(mayaRoot, "*").Where(x =>
                     Regex.IsMatch(x.Name, @"^\d{4}(-x64)?$", RegexOptions.IgnoreCase)))
        {
            AddIfPath(result, "maya", "Autodesk Maya", version.Name, "Version plug-ins",
                Path.Combine(version.FullName, "plug-ins"), true,
                "Maya plug-ins installed for this user and version.");
            AddIfPath(result, "maya", "Autodesk Maya", version.Name, "Version modules",
                Path.Combine(version.FullName, "modules"), true,
                "Maya module packages installed for this user and version.");
            AddMayaEnvironmentFilePaths(result, Path.Combine(version.FullName, "Maya.env"), mayaRoot,
                version.Name);
        }
    }

    private static void AddMayaEnvironmentFilePaths(List<Candidate> result, string file, string mayaRoot,
        string version)
    {
        if (!File.Exists(file)) return;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var variable = line[..separator].Trim();
                if (!variable.Equals("MAYA_PLUG_IN_PATH", StringComparison.OrdinalIgnoreCase) &&
                    !variable.Equals("MAYA_MODULE_PATH", StringComparison.OrdinalIgnoreCase)) continue;
                var value = line[(separator + 1)..]
                    .Replace("%MAYA_APP_DIR%", mayaRoot, StringComparison.OrdinalIgnoreCase)
                    .Replace("$MAYA_APP_DIR", mayaRoot, StringComparison.OrdinalIgnoreCase);
                foreach (var path in ResolvePathList(value))
                    AddIfPath(result, "maya", "Autodesk Maya", version,
                        variable.Contains("MODULE", StringComparison.OrdinalIgnoreCase)
                            ? "Maya.env module path" : "Maya.env plug-in path",
                        path, true, "External Maya plug-in location configured in Maya.env.");
            }
        }
        catch { }
    }

    private static void AddCinema4D(List<Candidate> result)
    {
        foreach (var install in ProgramRoots().SelectMany(root => SafeDirectories(root, "*Cinema 4D*"))
                     .DistinctBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
            AddIfPath(result, "cinema4d", "Cinema 4D", LastVersion(install.Name), "Installation plug-ins",
                Path.Combine(install.FullName, "plugins"), false,
                "Plug-ins installed beside Cinema 4D. Native modules may be version-specific; administrator rights may be required to restore.");

        AddEnvironmentPaths(result, "cinema4d", "Cinema 4D", "shared", "External plug-ins",
            "C4D_PLUGINS_DIR", true, "External Cinema 4D plug-ins configured through C4D_PLUGINS_DIR.");
        AddEnvironmentPaths(result, "cinema4d", "Cinema 4D", "shared", "Additional modules",
            "g_additionalModulePath", true,
            "Additional Cinema 4D module paths configured through g_additionalModulePath.");
    }

    private static void AddNukeAndOpenFx(List<Candidate> result)
    {
        AddEnvironmentPaths(result, "nuke", "Nuke", "shared", "External NUKE_PATH plug-ins",
            "NUKE_PATH", true, "External Nuke plug-ins, gizmos, and scripts configured through NUKE_PATH.");
        foreach (var common in CommonProgramRoots())
        {
            AddIfPath(result, "nuke", "Nuke", "shared", "Shared NDK plug-ins",
                Path.Combine(common, "NUKE"), false,
                "Machine-wide Nuke binary plug-ins. Native plug-ins are version-specific.");
            AddIfPath(result, "openfx-shared", "OpenFX shared plug-ins", "shared", "OpenFX plug-ins",
                Path.Combine(common, "OFX", "Plugins"), true,
                "Machine-wide OpenFX plug-ins used by Nuke and other compatible hosts.");
        }
        AddEnvironmentPaths(result, "openfx-shared", "OpenFX shared plug-ins", "shared",
            "External OpenFX plug-ins", "OFX_PLUGIN_PATH", true,
            "Additional OpenFX plug-in roots configured through OFX_PLUGIN_PATH.");
    }

    private static void AddPaintDotNet(List<Candidate> result, string documents)
    {
        AddIfPath(result, "paintdotnet", "paint.net", "shared", "User plug-ins and shapes",
            Path.Combine(documents, "paint.net App Files"), true,
            "Per-user Effects, FileTypes, and Shapes for classic, Store, and portable-compatible installs.");
        foreach (var root in ProgramRoots())
        {
            var install = Path.Combine(root, "paint.net");
            foreach (var folder in new[] { "Effects", "FileTypes", "Shapes" })
                AddIfPath(result, "paintdotnet", "paint.net", "shared", "Installation " + folder,
                    Path.Combine(install, folder), false,
                    "Classic paint.net installation content. Administrator rights may be required to restore.");
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\paint.net");
            var value = Convert.ToString(key?.GetValue("Plugins/AdditionalPluginDirectoryRoots"));
            foreach (var path in ResolvePathList(value))
                AddIfPath(result, "paintdotnet", "paint.net", "shared", "Additional plug-in root",
                    path, true, "Custom paint.net plug-in root configured in the registry.");
        }
        catch { }
    }

    private static void AddUnreal(List<Candidate> result, string programData)
    {
        foreach (var install in UnrealInstallRoots(programData))
            AddIfPath(result, "unreal", "Unreal Engine", LastVersion(install), "Engine Marketplace plug-ins",
                Path.Combine(install, "Engine", "Plugins", "Marketplace"), true,
                "Fab/Marketplace and third-party plug-ins installed for this engine version. May be large and version-specific.",
                ["Intermediate", "DerivedDataCache"]);
    }

    private static IEnumerable<string> UnrealInstallRoots(string programData)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ProgramRoots())
        foreach (var install in SafeDirectories(Path.Combine(root, "Epic Games"), "UE_*"))
            result.Add(install.FullName);

        var manifests = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(manifests))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(file));
                        if (!document.RootElement.TryGetProperty("AppName", out var appName)) continue;
                        var name = appName.GetString();
                        if (string.IsNullOrWhiteSpace(name) ||
                            !name.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) ||
                            !document.RootElement.TryGetProperty("InstallLocation", out var location)) continue;
                        var path = location.GetString();
                        if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                    }
                    catch { }
                }
            }
            catch { }
        }
        return result;
    }

    private static void AddEnvironmentPaths(List<Candidate> result, string appId, string product,
        string version, string category, string variable, bool recommended, string notes)
    {
        foreach (var path in ResolvePathList(Environment.GetEnvironmentVariable(variable)))
            AddIfPath(result, appId, product, version, category, path, recommended, notes);
    }

    private static IEnumerable<string> ResolvePathList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var item in value.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = item.Trim().Trim('"');
            if (path == "&") continue;
            path = path.Replace("${HOME}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    StringComparison.OrdinalIgnoreCase)
                .Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    StringComparison.OrdinalIgnoreCase);
            path = Regex.Replace(path, @"\$\{?(?<name>[A-Za-z_][A-Za-z0-9_]*)\}?", match =>
                Environment.GetEnvironmentVariable(match.Groups["name"].Value) ?? match.Value);
            path = Environment.ExpandEnvironmentVariables(path);
            if (path.Contains('$') || path.Contains('&') || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                continue;
            string? fullPath = null;
            try { fullPath = Path.GetFullPath(path); }
            catch { }
            if (fullPath != null) yield return fullPath;
        }
    }

    private static void AddIfPath(List<Candidate> result, string appId, string product, string version,
        string category, string path, bool recommended, string notes,
        IReadOnlyCollection<string>? exclusions = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            result.Add(new Candidate(appId, product,
                string.IsNullOrWhiteSpace(version) ? "shared" : version,
                category, Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)), recommended, notes,
                exclusions));
        }
        catch { }
    }

    private static (int Count, long Bytes) Measure(string root, IReadOnlyCollection<string> exclusions)
    {
        var count = 0;
        long bytes = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (DiscoveryService.IsExcluded(relative, exclusions)) continue;
                try
                {
                    count++;
                    bytes += new FileInfo(file).Length;
                }
                catch { }
            }
        }
        catch { }
        return (count, bytes);
    }

    private static IEnumerable<string> ProgramRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> CommonProgramRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

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

    private static string LastVersion(string text)
    {
        var matches = Regex.Matches(text, @"\d+(?:\.\d+)*");
        return matches.Count == 0 ? "shared" : matches[^1].Value;
    }
}
