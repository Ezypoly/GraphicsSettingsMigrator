using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphicsSettingsMigrator;

public static class JsonSupport
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class BackupService
{
    public async Task<string> CreateBackupAsync(
        IReadOnlyCollection<SettingsLocation> selected,
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (selected.Count == 0) throw new InvalidOperationException("No settings sets selected for backup.");
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new InvalidOperationException("No destination folder selected.");

        Directory.CreateDirectory(destinationRoot);
        var packageRoot = UniqueDirectory(destinationRoot,
            "GraphicsSettingsBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(packageRoot);
        var payloadRoot = Path.Combine(packageRoot, "payload");
        Directory.CreateDirectory(payloadRoot);

        var manifest = new BackupManifest();
        var index = 0;
        foreach (var location in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report("Copying " + index + "/" + selected.Count + ": " +
                             location.Product + " — " + location.Category);

            var entryPayload = Path.Combine(payloadRoot, location.Id);
            var entry = await CaptureEntryAsync(location, entryPayload, location.Id,
                Path.Combine("payload", location.Id).Replace('\\', '/'), cancellationToken);
            manifest.Entries.Add(entry);
        }

        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, JsonSupport.Options), cancellationToken);
        progress?.Report("Done: " + packageRoot);
        return packageRoot;
    }

    internal static async Task<BackupEntry> CaptureEntryAsync(
        SettingsLocation location,
        string entryPayload,
        string entryId,
        string payloadPath,
        CancellationToken cancellationToken)
    {
        var entry = new BackupEntry
        {
            Id = entryId,
            AppId = location.AppId,
            Product = location.Product,
            SourceVersion = location.Version,
            Category = location.Category,
            Kind = location.Kind,
            OriginalPath = location.SourcePath,
            PortablePath = location.PortablePath,
            PayloadPath = payloadPath.Replace('\\', '/'),
            Notes = location.Notes
        };
        Directory.CreateDirectory(entryPayload);

        if (location.Kind == SourceKind.Registry)
        {
            var snapshot = RegistryTransfer.Capture(location.SourcePath)
                ?? throw new InvalidOperationException("Could not read " + location.SourcePath);
            var registryFile = Path.Combine(entryPayload, "registry.json");
            await File.WriteAllTextAsync(registryFile,
                JsonSerializer.Serialize(snapshot, JsonSupport.Options), cancellationToken);
            var info = new FileInfo(registryFile);
            entry.Files.Add(new BackupFile
            {
                RelativePath = "registry.json",
                SizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = await HashFileAsync(registryFile, cancellationToken)
            });
        }
        else if (location.Kind == SourceKind.File)
        {
            await CopyOneToBackupAsync(location.SourcePath, entryPayload, Path.GetFileName(location.SourcePath),
                entry, cancellationToken);
        }
        else
        {
            foreach (var sourceFile in SafeEnumerateFiles(location.SourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(location.SourcePath, sourceFile);
                if (DiscoveryService.IsExcluded(relative, location.ExcludedPrefixes)) continue;
                await CopyOneToBackupAsync(sourceFile, entryPayload, relative, entry, cancellationToken);
            }
        }

        entry.FileCount = entry.Files.Count;
        entry.SizeBytes = entry.Files.Sum(x => x.SizeBytes);
        return entry;
    }

    private static async Task CopyOneToBackupAsync(string sourceFile, string payloadRoot, string relative,
        BackupEntry entry, CancellationToken cancellationToken)
    {
        var destination = SafeChildPath(payloadRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                         1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var sourceInfo = new FileInfo(sourceFile);
        File.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
        var targetInfo = new FileInfo(destination);
        entry.Files.Add(new BackupFile
        {
            RelativePath = relative.Replace('\\', '/'),
            SizeBytes = targetInfo.Length,
            LastWriteUtc = sourceInfo.LastWriteTimeUtc,
            Sha256 = await HashFileAsync(destination, cancellationToken)
        });
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    internal static string SafeChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe relative path: " + relative);
        return fullPath;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception ex)
        {
            throw new IOException("Could not read directory " + root, ex);
        }
    }

    private static string UniqueDirectory(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var number = 2;
        while (Directory.Exists(candidate))
            candidate = Path.Combine(parent, name + "_" + number++);
        return candidate;
    }
}

public sealed class RestoreService
{
    public async Task<BackupManifest> LoadManifestAsync(string packageRoot,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The selected folder does not contain manifest.json.", manifestPath);
        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(json, JsonSupport.Options)
            ?? throw new InvalidDataException("The manifest is invalid.");
        if (manifest.FormatVersion != 1)
            throw new InvalidDataException("Unsupported manifest version: " + manifest.FormatVersion);
        return manifest;
    }

    public RestorePreview Preview(string packageRoot, IReadOnlyCollection<RestoreSelection> selections)
    {
        var result = new RestorePreview();
        foreach (var selection in selections)
        {
            var entry = selection.Entry;
            if (entry.Kind == SourceKind.Registry)
            {
                result.FilesToCopy++;
                if (RegistryTransfer.Exists(selection.TargetPath)) result.ExistingFiles++;
                continue;
            }

            foreach (var file in entry.Files)
            {
                var source = PayloadFile(packageRoot, entry, file.RelativePath);
                if (!File.Exists(source))
                {
                    result.MissingPayloadFiles++;
                    continue;
                }

                var destination = DestinationFile(selection.TargetPath, entry.Kind, file.RelativePath);
                result.FilesToCopy++;
                result.BytesToCopy += file.SizeBytes;
                if (File.Exists(destination)) result.ExistingFiles++;
            }

            if (!string.Equals(entry.SourceVersion, "shared", StringComparison.OrdinalIgnoreCase) &&
                !selection.TargetPath.Contains(entry.SourceVersion, StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(entry.Product + " " + entry.SourceVersion +
                    ": cross-version migration detected. Core binary preferences may be incompatible.");
            }
        }
        return result;
    }

    public async Task<RestoreResult> RestoreAsync(
        string packageRoot,
        IReadOnlyCollection<RestoreSelection> selections,
        bool overwrite,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0) throw new InvalidOperationException("No settings sets selected for restore.");
        var rollbackRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GraphicsSettingsMigrator Rollbacks",
            DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(rollbackRoot);

        var rollbackManifest = new RollbackManifest
        {
            SourcePackage = packageRoot
        };
        await RollbackService.SaveManifestAsync(rollbackRoot, rollbackManifest, cancellationToken);

        var result = new RestoreResult { RollbackPath = rollbackRoot };
        var selectionIndex = 0;
        foreach (var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            selectionIndex++;
            progress?.Report("Restoring " + selectionIndex + "/" + selections.Count + ": " +
                             selection.Entry.Product + " — " + selection.Entry.Category);

            var entry = selection.Entry;
            var entryRollback = BackupService.SafeChildPath(rollbackRoot, entry.Id);
            Directory.CreateDirectory(entryRollback);
            var rollbackEntry = new RollbackEntry
            {
                AppId = entry.AppId,
                Product = entry.Product,
                SourceVersion = entry.SourceVersion,
                Category = entry.Category,
                Kind = entry.Kind,
                TargetPath = selection.TargetPath,
                BackupDirectory = entry.Id
            };
            rollbackManifest.Entries.Add(rollbackEntry);

            if (entry.Kind == SourceKind.Registry)
            {
                var source = PayloadFile(packageRoot, entry, "registry.json");
                await VerifyFileAsync(source, entry.Files.FirstOrDefault(), cancellationToken);
                var existing = RegistryTransfer.Capture(selection.TargetPath);
                if (existing != null)
                {
                    await File.WriteAllTextAsync(Path.Combine(entryRollback, "registry-before.json"),
                        JsonSerializer.Serialize(existing, JsonSupport.Options), cancellationToken);
                }
                rollbackEntry.RegistryExistedBefore = existing != null;
                await RollbackService.SaveManifestAsync(
                    rollbackRoot, rollbackManifest, cancellationToken);
                var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(
                    await File.ReadAllTextAsync(source, cancellationToken), JsonSupport.Options)
                    ?? throw new InvalidDataException("The registry snapshot is invalid.");
                RegistryTransfer.Restore(selection.TargetPath, snapshot, overwrite);
                result.CopiedFiles++;
                continue;
            }

            foreach (var file in entry.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = PayloadFile(packageRoot, entry, file.RelativePath);
                await VerifyFileAsync(source, file, cancellationToken);
                var destination = DestinationFile(selection.TargetPath, entry.Kind, file.RelativePath);

                if (File.Exists(destination))
                {
                    if (!overwrite)
                    {
                        result.SkippedFiles++;
                        continue;
                    }

                    var rollbackFile = BackupService.SafeChildPath(entryRollback, file.RelativePath);
                    var previousLastWriteUtc = File.GetLastWriteTimeUtc(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(rollbackFile)!);
                    File.Copy(destination, rollbackFile, true);
                    File.SetLastWriteTimeUtc(rollbackFile, previousLastWriteUtc);
                    rollbackEntry.Files.Add(new RollbackFile
                    {
                        RelativePath = file.RelativePath,
                        ExistedBefore = true,
                        PreviousLastWriteUtc = previousLastWriteUtc,
                        AppliedSha256 = file.Sha256
                    });
                }
                else
                    rollbackEntry.Files.Add(new RollbackFile
                    {
                        RelativePath = file.RelativePath,
                        ExistedBefore = false,
                        AppliedSha256 = file.Sha256
                    });

                await RollbackService.SaveManifestAsync(
                    rollbackRoot, rollbackManifest, cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite);
                File.SetLastWriteTimeUtc(destination, file.LastWriteUtc);
                result.CopiedFiles++;
            }
        }

        rollbackManifest.CompletedUtc = DateTime.UtcNow;
        await RollbackService.SaveManifestAsync(rollbackRoot, rollbackManifest, cancellationToken);

        var note = "A rollback backup was created automatically before restore." + Environment.NewLine +
                   "Source: " + packageRoot + Environment.NewLine +
                   "UTC time: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await File.WriteAllTextAsync(Path.Combine(rollbackRoot, "README.txt"), note, cancellationToken);
        progress?.Report("Restore completed. Rollback: " + rollbackRoot);
        return result;
    }

    public static List<string> FindRunningGraphicsApps()
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Photoshop"] = "Adobe Photoshop",
            ["Illustrator"] = "Adobe Illustrator",
            ["AfterFX"] = "Adobe After Effects",
            ["Adobe Media Encoder"] = "Adobe Media Encoder",
            ["ZBrush"] = "Maxon ZBrush",
            ["3DCoat"] = "3DCoat",
            ["blender"] = "Blender",
            ["maya"] = "Autodesk Maya",
            ["3dsmax"] = "Autodesk 3ds Max",
            ["Cinema 4D"] = "Cinema 4D",
            ["houdini"] = "Houdini",
            ["houdinifx"] = "Houdini FX",
            ["Rhino"] = "Rhino",
            ["SketchUp"] = "SketchUp",
            ["Nuke"] = "Nuke",
            ["Mari"] = "Mari",
            ["modo"] = "Modo",
            ["Marmoset Toolbag"] = "Marmoset Toolbag",
            ["MarvelousDesigner"] = "Marvelous Designer",
            ["CLO"] = "CLO",
            ["keyshot"] = "KeyShot",
            ["UnrealEditor"] = "Unreal Engine",
            ["Unity"] = "Unity",
            ["Godot"] = "Godot",
            ["krita"] = "Krita",
            ["gimp-3.0"] = "GIMP",
            ["gimp-2.10"] = "GIMP",
            ["inkscape"] = "Inkscape",
            ["AffinityPhoto2"] = "Affinity Photo",
            ["AffinityDesigner2"] = "Affinity Designer",
            ["AffinityPublisher2"] = "Affinity Publisher",
            ["Plasticity"] = "Plasticity",
            ["Adobe Substance 3D Painter"] = "Substance Painter",
            ["Adobe Substance 3D Designer"] = "Substance Designer",
            ["Adobe Substance 3D Modeler"] = "Substance Modeler",
            ["Adobe Substance 3D Sampler"] = "Substance Sampler"
        };
        var running = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (known.TryGetValue(process.ProcessName, out var product)) running.Add(product);
            }
            catch { }
            finally { process.Dispose(); }
        }
        return running.OrderBy(x => x).ToList();
    }

    private static string PayloadFile(string packageRoot, BackupEntry entry, string relative)
    {
        var payloadRoot = BackupService.SafeChildPath(packageRoot, entry.PayloadPath);
        return BackupService.SafeChildPath(payloadRoot, relative);
    }

    private static string DestinationFile(string targetPath, SourceKind kind, string relative)
    {
        if (kind == SourceKind.File) return targetPath;
        return BackupService.SafeChildPath(targetPath, relative);
    }

    private static async Task VerifyFileAsync(string source, BackupFile? expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("A payload file is missing from the backup.", source);
        if (expected == null || string.IsNullOrWhiteSpace(expected.Sha256)) return;
        var actual = await BackupService.HashFileAsync(source, cancellationToken);
        if (!actual.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Checksum mismatch: " + source);
    }
}

public static class RegistryTransfer
{
    public static bool Exists(string path)
    {
        try
        {
            using var key = Open(path, writable: false, create: false);
            return key != null;
        }
        catch { return false; }
    }

    public static RegistrySnapshot? Capture(string path)
    {
        using var key = Open(path, writable: false, create: false);
        return key == null ? null : CaptureKey(key, path);
    }

    public static void Restore(string targetPath, RegistrySnapshot snapshot, bool overwrite)
    {
        using var key = Open(targetPath, writable: true, create: true)
            ?? throw new InvalidOperationException("Could not create registry key " + targetPath);
        RestoreKey(key, snapshot, overwrite);
    }
    public static void Delete(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only HKCU is supported: " + path);
        var subPath = normalized[5..].Trim('\\');
        if (string.IsNullOrWhiteSpace(subPath))
            throw new InvalidOperationException("The HKCU root cannot be deleted.");
        Registry.CurrentUser.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
    }


    private static RegistrySnapshot CaptureKey(RegistryKey key, string path)
    {
        var result = new RegistrySnapshot { KeyPath = path };
        foreach (var name in key.GetValueNames())
        {
            var kind = key.GetValueKind(name);
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            result.Values.Add(ToSnapshot(name, kind, value));
        }
        foreach (var subName in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(subName);
            if (sub != null) result.SubKeys.Add(CaptureKey(sub, path + "\\" + subName));
        }
        return result;
    }

    private static RegistryValueSnapshot ToSnapshot(string name, RegistryValueKind kind, object? value)
    {
        var item = new RegistryValueSnapshot { Name = name, Kind = kind };
        switch (kind)
        {
            case RegistryValueKind.Binary:
            case RegistryValueKind.None:
                item.Data.Add(Convert.ToBase64String((byte[]?)value ?? []));
                break;
            case RegistryValueKind.MultiString:
                item.Data.AddRange((string[]?)value ?? []);
                break;
            default:
                item.Data.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                break;
        }
        return item;
    }

    private static void RestoreKey(RegistryKey key, RegistrySnapshot snapshot, bool overwrite)
    {
        foreach (var value in snapshot.Values)
        {
            if (!overwrite && key.GetValueNames().Contains(value.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            object data = value.Kind switch
            {
                RegistryValueKind.Binary or RegistryValueKind.None =>
                    Convert.FromBase64String(value.Data.FirstOrDefault() ?? ""),
                RegistryValueKind.MultiString => value.Data.ToArray(),
                RegistryValueKind.DWord => int.Parse(value.Data.FirstOrDefault() ?? "0",
                    CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => long.Parse(value.Data.FirstOrDefault() ?? "0",
                    CultureInfo.InvariantCulture),
                _ => value.Data.FirstOrDefault() ?? ""
            };
            key.SetValue(value.Name, data, value.Kind);
        }

        foreach (var subSnapshot in snapshot.SubKeys)
        {
            var subName = subSnapshot.KeyPath.Split('\\').Last();
            using var subKey = key.CreateSubKey(subName, writable: true);
            RestoreKey(subKey, subSnapshot, overwrite);
        }
    }

    private static RegistryKey? Open(string path, bool writable, bool create)
    {
        var normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only HKCU is supported: " + path);
        var subPath = normalized[5..];
        return create
            ? Registry.CurrentUser.CreateSubKey(subPath, writable: true)
            : Registry.CurrentUser.OpenSubKey(subPath, writable);
    }
}
