using System.Text.Json;

namespace GraphicsSettingsMigrator;

internal sealed class RemovalResult
{
    public string RecoveryBackupPath { get; init; } = "";
    public int RemovedFiles { get; set; }
    public int RemovedRegistryKeys { get; set; }
    public List<string> Failures { get; } = [];
}

internal sealed class RemovalService
{
    private readonly BackupService _backupService = new();

    public async Task<RemovalResult> RemoveAsync(IReadOnlyCollection<SettingsLocation> locations,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (locations.Count == 0) throw new InvalidOperationException("No settings sets selected for removal.");

        var recoveryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GraphicsSettingsMigrator Removed Settings");
        progress?.Report("Creating a recovery backup before removal...");
        var packageRoot = await _backupService.CreateBackupAsync(
            locations, recoveryRoot, progress, cancellationToken);
        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(
                           await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonSupport.Options)
                       ?? throw new InvalidDataException("The recovery backup manifest is invalid.");

        var result = new RemovalResult { RecoveryBackupPath = packageRoot };
        var index = 0;
        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report("Removing " + index + "/" + locations.Count + ": " +
                             location.Product + " — " + location.Version + " — " + location.Category);
            try
            {
                var entry = manifest.Entries.SingleOrDefault(x => x.Id == location.Id)
                    ?? throw new InvalidDataException("A settings set is missing from the recovery backup.");
                if (location.Kind == SourceKind.Registry)
                {
                    RegistryTransfer.Delete(location.SourcePath);
                    result.RemovedRegistryKeys++;
                    continue;
                }

                if (location.Kind == SourceKind.File)
                {
                    var backupFile = entry.Files.SingleOrDefault()
                        ?? throw new InvalidDataException("The recovery backup does not contain the source file.");
                    if (await DeleteVerifiedFileAsync(location.SourcePath, backupFile, cancellationToken))
                        result.RemovedFiles++;
                    else
                        result.Failures.Add(location.SourcePath + " changed after backup and was not removed.");
                    continue;
                }

                foreach (var backupFile in entry.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceFile = BackupService.SafeChildPath(location.SourcePath, backupFile.RelativePath);
                    try
                    {
                        if (!File.Exists(sourceFile)) continue;
                        if (await DeleteVerifiedFileAsync(sourceFile, backupFile, cancellationToken))
                            result.RemovedFiles++;
                        else
                            result.Failures.Add(sourceFile + " changed after backup and was not removed.");
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add(sourceFile + ": " + ex.Message);
                    }
                }
                RemoveEmptyDirectories(location.SourcePath);
            }
            catch (Exception ex)
            {
                result.Failures.Add(location.Product + " " + location.Version + " — " +
                                    location.Category + ": " + ex.Message);
            }
        }

        progress?.Report("Removal completed. Recovery backup: " + packageRoot);
        return result;
    }

    private static async Task<bool> DeleteVerifiedFileAsync(string path, BackupFile backupFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return true;
        var currentHash = await BackupService.HashFileAsync(path, cancellationToken);
        if (!currentHash.Equals(backupFile.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        File.Delete(path);
        return true;
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(x => x.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch { }
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch { }
    }
}
