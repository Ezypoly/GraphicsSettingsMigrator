using System.Text.RegularExpressions;

namespace GraphicsSettingsMigrator;

/// <summary>
/// Finds user-created content that applications keep outside their normal settings profile.
/// These locations commonly contain scripts, plug-ins, presets, brushes, packages, and libraries.
/// </summary>
internal static class CustomContentDiscovery
{
    public static void AddExisting(List<SettingsLocation> result, Func<string, string> portable)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        AddAdobeInstallContent(result, portable);
        AddAdobeSharedContent(result, portable, roaming);
        AddAdobeDocumentContent(result, portable, documents);
        AddMayaContent(result, portable, documents);
        AddBlenderExternalContent(result, portable, roaming);
        AddRhinoAndGrasshopperContent(result, portable, roaming);

        // Legacy Painter shelves can still be used after an Adobe-era upgrade.
        AddDirectory(result, portable, "substance-painter", "Adobe Substance 3D Painter", "legacy",
            "Legacy shelf", Path.Combine(documents, "Allegorithmic", "Substance Painter", "shelf"), true,
            "Legacy custom materials, brushes, alphas, filters, generators, and presets.");
    }

    public static void AddTargets(List<TargetLocation> targets)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foreach (var adobeRoot in AdobeProgramRoots())
        {
            foreach (var install in SafeDirectories(adobeRoot, "Adobe Photoshop *"))
            {
                var version = install.Name["Adobe Photoshop ".Length..];
                AddTarget(targets, "photoshop", "Adobe Photoshop", version,
                    "Installation presets and scripts", Path.Combine(install.FullName, "Presets"));
                AddTarget(targets, "photoshop", "Adobe Photoshop", version,
                    "Installation plug-ins", Path.Combine(install.FullName, "Plug-ins"));
            }

            foreach (var install in SafeDirectories(adobeRoot, "Adobe After Effects *"))
            {
                var version = LastVersion(install.Name);
                var support = Path.Combine(install.FullName, "Support Files");
                AddTarget(targets, "aftereffects", "Adobe After Effects", version,
                    "Installation scripts", Path.Combine(support, "Scripts"));
                AddTarget(targets, "aftereffects", "Adobe After Effects", version,
                    "Installation presets", Path.Combine(support, "Presets"));
                AddTarget(targets, "aftereffects", "Adobe After Effects", version,
                    "Installation plug-ins", Path.Combine(support, "Plug-ins"));
                AddTarget(targets, "aftereffects", "Adobe After Effects", version,
                    "User presets and libraries", Path.Combine(documents, "Adobe", install.Name));
            }

            foreach (var install in SafeDirectories(adobeRoot, "Adobe Illustrator *"))
            {
                var version = LastVersion(install.Name);
                var windows = Path.Combine(install.FullName, "Support Files", "Contents", "Windows");
                AddTarget(targets, "illustrator", "Adobe Illustrator", version,
                    "Installation presets and scripts", Path.Combine(windows, "Presets"));
                AddTarget(targets, "illustrator", "Adobe Illustrator", version,
                    "Installation plug-ins", Path.Combine(windows, "Plug-ins"));
            }
        }

        AddTarget(targets, "adobe-shared", "Adobe shared components", "shared", "User CEP extensions",
            Path.Combine(roaming, "Adobe", "CEP", "extensions"));
        AddTarget(targets, "adobe-shared", "Adobe shared components", "shared", "UXP plug-in data",
            Path.Combine(roaming, "Adobe", "UXP", "PluginsStorage"));
        AddTarget(targets, "adobe-shared", "Adobe shared components", "shared", "Shared color settings",
            Path.Combine(roaming, "Adobe", "Color"));

        AddTarget(targets, "maya", "Autodesk Maya", "shared", "Modules",
            Path.Combine(MayaRoot(documents), "modules"));
        AddTarget(targets, "maya", "Autodesk Maya", "shared", "User plug-ins",
            Path.Combine(MayaRoot(documents), "plug-ins"));
        AddTarget(targets, "maya", "Autodesk Maya", "shared", "Icons",
            Path.Combine(MayaRoot(documents), "icons"));

        AddTarget(targets, "rhino", "Rhino", "shared", "Package Manager packages",
            Path.Combine(roaming, "McNeel", "Rhinoceros", "packages"));
        foreach (var programFiles in ProgramRoots())
        foreach (var install in SafeDirectories(programFiles, "Rhino *"))
        {
            var major = LastVersion(install.Name).Split('.')[0];
            if (int.TryParse(major, out _))
                AddTarget(targets, "rhino", "Rhino", major + ".0", "User plug-ins",
                    Path.Combine(roaming, "McNeel", "Rhinoceros", major + ".0", "Plug-ins"));
        }
        AddTarget(targets, "grasshopper", "Grasshopper", "shared", "User components and settings",
            Path.Combine(roaming, "Grasshopper"));

        AddTarget(targets, "substance-painter", "Adobe Substance 3D Painter", "shared", "User content",
            Path.Combine(documents, "Adobe", "Adobe Substance 3D Painter"));
        AddTarget(targets, "substance-designer", "Adobe Substance 3D Designer", "shared", "User content",
            Path.Combine(documents, "Adobe", "Adobe Substance 3D Designer"));
        AddTarget(targets, "substance-modeler", "Adobe Substance 3D Modeler", "shared", "Stamps",
            Path.Combine(documents, "Adobe", "Adobe Substance 3D Modeler", "Stamps"));
        AddTarget(targets, "substance-sampler", "Adobe Substance 3D Sampler", "shared", "User assets",
            Path.Combine(documents, "Adobe", "Adobe Substance 3D Sampler", "yourAssets"));
    }

    private static void AddAdobeInstallContent(List<SettingsLocation> result, Func<string, string> portable)
    {
        foreach (var adobeRoot in AdobeProgramRoots())
        {
            foreach (var install in SafeDirectories(adobeRoot, "Adobe Photoshop *"))
            {
                var version = install.Name["Adobe Photoshop ".Length..];
                AddDirectory(result, portable, "photoshop", "Adobe Photoshop", version,
                    "Installation presets and scripts", Path.Combine(install.FullName, "Presets"), true,
                    "Complete application presets, including custom JSX/JS scripts, actions, brushes, swatches, and styles. Administrator rights may be required to restore.");
                AddDirectory(result, portable, "photoshop", "Adobe Photoshop", version,
                    "Installation plug-ins", Path.Combine(install.FullName, "Plug-ins"), true,
                    "Installed third-party and custom plug-ins. Native plug-ins may be version-specific; administrator rights may be required to restore.");
            }

            foreach (var install in SafeDirectories(adobeRoot, "Adobe After Effects *"))
            {
                var version = LastVersion(install.Name);
                var support = Path.Combine(install.FullName, "Support Files");
                AddDirectory(result, portable, "aftereffects", "Adobe After Effects", version,
                    "Installation scripts", Path.Combine(support, "Scripts"), true,
                    "Scripts, ScriptUI panels, and startup/shutdown scripts installed with After Effects.");
                AddDirectory(result, portable, "aftereffects", "Adobe After Effects", version,
                    "Installation presets", Path.Combine(support, "Presets"), true,
                    "Animation presets installed in the application folder.");
                AddDirectory(result, portable, "aftereffects", "Adobe After Effects", version,
                    "Installation plug-ins", Path.Combine(support, "Plug-ins"), true,
                    "Third-party and custom plug-ins; native plug-ins may be version-specific.");
            }

            foreach (var install in SafeDirectories(adobeRoot, "Adobe Illustrator *"))
            {
                var version = LastVersion(install.Name);
                var windows = Path.Combine(install.FullName, "Support Files", "Contents", "Windows");
                AddDirectory(result, portable, "illustrator", "Adobe Illustrator", version,
                    "Installation presets and scripts", Path.Combine(windows, "Presets"), true,
                    "Complete application presets, actions, workspaces, and installed scripts.");
                AddDirectory(result, portable, "illustrator", "Adobe Illustrator", version,
                    "Installation plug-ins", Path.Combine(windows, "Plug-ins"), true,
                    "Third-party and custom plug-ins; native plug-ins may be version-specific.");
            }
        }

        foreach (var common in AdobeCommonRoots())
        {
            var suffix = common.Equals(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
                StringComparison.OrdinalIgnoreCase) ? " (32-bit)" : " (64-bit)";
            AddDirectory(result, portable, "adobe-shared", "Adobe shared components", "shared",
                "Machine-wide CEP extensions" + suffix, Path.Combine(common, "Adobe", "CEP", "extensions"), true,
                "Machine-wide CEP panels and extensions. Administrator rights may be required to restore.");
            AddDirectory(result, portable, "adobe-shared", "Adobe shared components", "shared",
                "Shared plug-ins" + suffix, Path.Combine(common, "Adobe", "Plug-Ins", "CC"), true,
                "Shared Adobe and third-party plug-ins, including Camera Raw components. May be large and version-specific.");
        }
    }

    private static void AddAdobeSharedContent(List<SettingsLocation> result, Func<string, string> portable,
        string roaming)
    {
        var adobe = Path.Combine(roaming, "Adobe");
        AddDirectory(result, portable, "adobe-shared", "Adobe shared components", "shared",
            "User CEP extensions", Path.Combine(adobe, "CEP", "extensions"), true,
            "Per-user CEP panels and extensions.");
        AddDirectory(result, portable, "adobe-shared", "Adobe shared components", "shared",
            "UXP plug-in data", Path.Combine(adobe, "UXP", "PluginsStorage"), true,
            "Photoshop and Illustrator UXP plug-in settings and persistent data.",
            ["Cache", "Code Cache", "GPUCache", "Crashpad", "logs", "Temp", "EBWebView"]);
        AddDirectory(result, portable, "adobe-shared", "Adobe shared components", "shared",
            "Shared color settings", Path.Combine(adobe, "Color"), true,
            "Shared color settings, proof setups, profiles, and color books.");
    }

    private static void AddAdobeDocumentContent(List<SettingsLocation> result, Func<string, string> portable,
        string documents)
    {
        var adobeDocuments = Path.Combine(documents, "Adobe");
        foreach (var afterEffects in SafeDirectories(adobeDocuments, "After Effects *"))
            AddDirectory(result, portable, "aftereffects", "Adobe After Effects", LastVersion(afterEffects.Name),
                "User presets and libraries", afterEffects.FullName, true,
                "User Presets, user libraries, and related reusable content.",
                ["Adobe After Effects Auto-Save", "Disk Cache", "Caches", "Logs"]);

        AddDirectory(result, portable, "mediaencoder", "Adobe Media Encoder", "shared",
            "User presets and queues", Path.Combine(adobeDocuments, "Adobe Media Encoder"), true,
            "User encoding presets, preset groups, and queue-related reusable data.", ["Cache", "Logs"]);

        AddDirectory(result, portable, "substance-painter", "Adobe Substance 3D Painter", "shared",
            "User content", Path.Combine(adobeDocuments, "Adobe Substance 3D Painter"), true,
            "Assets, plug-ins, Python scripts, export presets, ICC profiles, swatches, and translations.",
            ["autosave", "cache", "logs"]);
        AddDirectory(result, portable, "substance-designer", "Adobe Substance 3D Designer", "shared",
            "User content", Path.Combine(adobeDocuments, "Adobe Substance 3D Designer"), true,
            "Python scripts, packages, resources, and other user-created Designer content.",
            ["autosave", "cache", "logs"]);
        AddDirectory(result, portable, "substance-modeler", "Adobe Substance 3D Modeler", "shared",
            "Stamps", Path.Combine(adobeDocuments, "Adobe Substance 3D Modeler", "Stamps"), true,
            "Custom Modeler stamps.");
        AddDirectory(result, portable, "substance-sampler", "Adobe Substance 3D Sampler", "shared",
            "User assets", Path.Combine(adobeDocuments, "Adobe Substance 3D Sampler", "yourAssets"), true,
            "Custom Sampler materials and imported assets.");
    }

    private static void AddMayaContent(List<SettingsLocation> result, Func<string, string> portable,
        string documents)
    {
        var root = MayaRoot(documents);
        AddDirectory(result, portable, "maya", "Autodesk Maya", "shared", "Modules",
            Path.Combine(root, "modules"), true, "User module packages, including scripts, icons, presets, and plug-ins.");
        AddDirectory(result, portable, "maya", "Autodesk Maya", "shared", "User plug-ins",
            Path.Combine(root, "plug-ins"), true, "User-installed Maya plug-ins.");
        AddDirectory(result, portable, "maya", "Autodesk Maya", "shared", "Icons",
            Path.Combine(root, "icons"), true, "Custom shelf and tool icons.");

        AddEnvironmentDirectories(result, portable, "maya", "Autodesk Maya", "shared", "External scripts",
            "MAYA_SCRIPT_PATH", "Extra script roots configured through MAYA_SCRIPT_PATH.");
        AddEnvironmentDirectories(result, portable, "maya", "Autodesk Maya", "shared", "External plug-ins",
            "MAYA_PLUG_IN_PATH", "Extra plug-in roots configured through MAYA_PLUG_IN_PATH.");
        AddEnvironmentDirectories(result, portable, "maya", "Autodesk Maya", "shared", "External modules",
            "MAYA_MODULE_PATH", "Extra module roots configured through MAYA_MODULE_PATH.");
    }

    private static void AddBlenderExternalContent(List<SettingsLocation> result, Func<string, string> portable,
        string roaming)
    {
        var defaultRoot = Path.Combine(roaming, "Blender Foundation", "Blender");
        AddEnvironmentDirectoryIfExternal(result, portable, "blender", "Blender", "shared", "External scripts",
            "BLENDER_USER_SCRIPTS", defaultRoot, "Scripts, add-ons, modules, and presets from BLENDER_USER_SCRIPTS.");
        AddEnvironmentDirectoryIfExternal(result, portable, "blender", "Blender", "shared", "External config",
            "BLENDER_USER_CONFIG", defaultRoot, "Preferences and startup files from BLENDER_USER_CONFIG.");
        AddEnvironmentDirectoryIfExternal(result, portable, "blender", "Blender", "shared", "External extensions",
            "BLENDER_USER_EXTENSIONS", defaultRoot, "Extension repositories and installed extensions from BLENDER_USER_EXTENSIONS.");
    }

    private static void AddRhinoAndGrasshopperContent(List<SettingsLocation> result,
        Func<string, string> portable, string roaming)
    {
        var rhino = Path.Combine(roaming, "McNeel", "Rhinoceros");
        AddDirectory(result, portable, "rhino", "Rhino", "shared", "Package Manager packages",
            Path.Combine(rhino, "packages"), true, "Packages installed through Rhino Package Manager.");
        foreach (var version in SafeDirectories(rhino, "*").Where(x => Regex.IsMatch(x.Name, @"^\d+\.\d+$")))
            AddDirectory(result, portable, "rhino", "Rhino", version.Name, "User plug-ins",
                Path.Combine(version.FullName, "Plug-ins"), true, "Per-user Rhino plug-ins and plug-in data.");

        AddDirectory(result, portable, "grasshopper", "Grasshopper", "shared", "User components and settings",
            Path.Combine(roaming, "Grasshopper"), true,
            "Grasshopper libraries, user objects, settings, snippets, and custom components.",
            ["Cache", "Logs"]);
    }

    private static void AddEnvironmentDirectories(List<SettingsLocation> result, Func<string, string> portable,
        string appId, string product, string version, string category, string variable, string notes)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var path in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            AddDirectory(result, portable, appId, product, version, category, path, true, notes);
    }

    private static void AddEnvironmentDirectoryIfExternal(List<SettingsLocation> result,
        Func<string, string> portable, string appId, string product, string version, string category,
        string variable, string defaultRoot, string notes)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(path) || IsWithin(path, defaultRoot)) return;
        AddDirectory(result, portable, appId, product, version, category, path, true, notes);
    }

    private static bool IsWithin(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static IEnumerable<string> AdobeProgramRoots()
    {
        return ProgramRoots().Select(x => Path.Combine(x, "Adobe"));
    }

    private static IEnumerable<string> ProgramRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> AdobeCommonRoots()
    {
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
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
        string category, string path)
    {
        targets.Add(new TargetLocation
        {
            AppId = appId,
            Product = product,
            Version = string.IsNullOrWhiteSpace(version) ? "shared" : version,
            Category = category,
            Kind = SourceKind.Directory,
            TargetPath = path,
            Exists = Directory.Exists(path)
        });
    }

    private static string LastVersion(string text)
    {
        var matches = Regex.Matches(text, @"\d+(?:\.\d+)*");
        return matches.Count == 0 ? "shared" : matches[^1].Value;
    }
}
