using Microsoft.Win32;
using System.Globalization;
using System.Text.Json;

namespace GraphicsSettingsMigrator;

public sealed class RollbackService
{
    public static string RollbackRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GraphicsSettingsMigrator Rollbacks");

    public IReadOnlyList<RollbackPackage> Discover()
    {
        if (!Directory.Exists(RollbackRoot)) return [];
        var result = new List<RollbackPackage>();
        foreach (var folder in SafeDirectories(RollbackRoot))
        {
            var manifestPath = Path.Combine(folder.FullName, "rollback-manifest.json");
            RollbackManifest? manifest = null;
            try
            {
                if (File.Exists(manifestPath))
                    manifest = JsonSerializer.Deserialize<RollbackManifest>(
                        File.ReadAllText(manifestPath), JsonSupport.Options);
            }
            catch { }

            result.Add(new RollbackPackage
            {
                FolderPath = folder.FullName,
                CreatedUtc = manifest?.CreatedUtc ?? folder.CreationTimeUtc,
                Manifest = manifest
            });
        }
        return result.OrderByDescending(x => x.CreatedUtc).ToList();
    }

    public async Task<RollbackRevertResult> RevertAsync(string rollbackFolder,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var fullRoot = Path.GetFullPath(RollbackRoot).TrimEnd(Path.DirectorySeparatorChar) +
                       Path.DirectorySeparatorChar;
        var fullFolder = Path.GetFullPath(rollbackFolder).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar;
        if (!fullFolder.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected rollback folder is outside the managed rollback location.");

        var manifestPath = Path.Combine(fullFolder, "rollback-manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                "This is a legacy rollback without a manifest. It can only be restored manually.");
        var manifest = JsonSerializer.Deserialize<RollbackManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonSupport.Options)
            ?? throw new InvalidDataException("The rollback manifest is invalid.");
        if (manifest.FormatVersion != 1)
            throw new InvalidDataException("Unsupported rollback format: " + manifest.FormatVersion);

        var result = new RollbackRevertResult();
        var index = 0;
        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report("Reverting " + index + "/" + manifest.Entries.Count + ": " +
                             entry.Product + " — " + entry.Category);

            if (entry.Kind == SourceKind.Registry)
            {
                DeleteRegistryTree(entry.TargetPath);
                if (entry.RegistryExistedBefore)
                {
                    var entryRollbackRoot = BackupService.SafeChildPath(fullFolder, entry.BackupDirectory);
                    var snapshotPath = BackupService.SafeChildPath(
                        entryRollbackRoot, "registry-before.json");
                    if (!File.Exists(snapshotPath))
                        throw new FileNotFoundException("A registry rollback snapshot is missing.", snapshotPath);
                    var snapshot = JsonSerializer.Deserialize<RegistrySnapshot>(
                        await File.ReadAllTextAsync(snapshotPath, cancellationToken), JsonSupport.Options)
                        ?? throw new InvalidDataException("A registry rollback snapshot is invalid.");
                    RegistryTransfer.Restore(entry.TargetPath, snapshot, overwrite: true);
                }
                result.RestoredRegistryKeys++;
                continue;
            }

            foreach (var file in entry.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = DestinationFile(entry.TargetPath, entry.Kind, file.RelativePath);
                if (File.Exists(destination) && !string.IsNullOrWhiteSpace(file.AppliedSha256))
                {
                    var currentHash = await BackupService.HashFileAsync(destination, cancellationToken);
                    if (!currentHash.Equals(file.AppliedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SkippedChangedFiles++;
                        continue;
                    }
                }
                else if (file.ExistedBefore && !File.Exists(destination))
                {
                    result.SkippedChangedFiles++;
                    continue;
                }
                if (file.ExistedBefore)
                {
                    var entryRollbackRoot = BackupService.SafeChildPath(fullFolder, entry.BackupDirectory);
                    var source = BackupService.SafeChildPath(
                        entryRollbackRoot, file.RelativePath);
                    if (!File.Exists(source))
                        throw new FileNotFoundException("A rollback file is missing.", source);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, true);
                    if (file.PreviousLastWriteUtc.HasValue)
                        File.SetLastWriteTimeUtc(destination, file.PreviousLastWriteUtc.Value);
                    result.RestoredFiles++;
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                    RemoveEmptyParents(Path.GetDirectoryName(destination), entry.TargetPath, entry.Kind);
                    result.RemovedFiles++;
                }
            }
        }

        manifest.RevertedUtc = DateTime.UtcNow;
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, JsonSupport.Options), cancellationToken);
        progress?.Report("Revert completed: " + rollbackFolder);
        return result;
    }

    public static async Task SaveManifestAsync(string rollbackRoot, RollbackManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(Path.Combine(rollbackRoot, "rollback-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonSupport.Options), cancellationToken);
    }

    private static string DestinationFile(string targetPath, SourceKind kind, string relativePath) =>
        kind == SourceKind.File ? targetPath : BackupService.SafeChildPath(targetPath, relativePath);

    private static void DeleteRegistryTree(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only HKCU rollback is supported: " + path);
        Registry.CurrentUser.DeleteSubKeyTree(normalized[5..], throwOnMissingSubKey: false);
    }

    private static void RemoveEmptyParents(string? directory, string targetPath, SourceKind kind)
    {
        if (kind != SourceKind.Directory || directory == null) return;
        var root = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);
        var current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        while (current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) break;
            Directory.Delete(current);
            current = Path.GetDirectoryName(current)?.TrimEnd(Path.DirectorySeparatorChar) ?? root;
        }
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(string path)
    {
        try { return new DirectoryInfo(path).EnumerateDirectories().ToArray(); }
        catch { return []; }
    }
}
