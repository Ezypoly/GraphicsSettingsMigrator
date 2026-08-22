using System.Globalization;
using System.Text.Json;

namespace GraphicsSettingsMigrator;

public sealed class BackupUpdateService
{
    private readonly RestoreService _restoreService = new();

    public async Task<BackupUpdateResult> UpdateAsync(
        string packageRoot,
        IReadOnlyCollection<BackupUpdateSelection> selections,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (selections.Count == 0)
            throw new InvalidOperationException("No backup contents selected for update.");

        packageRoot = ValidatePackageRoot(packageRoot);
        var manifest = await _restoreService.LoadManifestAsync(packageRoot, cancellationToken);
        var parent = Directory.GetParent(packageRoot)?.FullName
            ?? throw new InvalidOperationException("The backup folder must have a parent folder.");
        var packageName = Path.GetFileName(packageRoot);
        var operationId = Guid.NewGuid().ToString("N");
        var workRoot = Path.Combine(parent, "." + packageName + ".update-" + operationId);
        var inspectionRoot = Path.Combine(workRoot, "inspection");
        var captureRoot = Path.Combine(workRoot, "capture");
        var stagingRoot = Path.Combine(workRoot, "staged-package");
        Directory.CreateDirectory(inspectionRoot);
        Directory.CreateDirectory(captureRoot);

        var result = new BackupUpdateResult { PackagePath = packageRoot };
        var changed = new List<ChangedEntry>();
        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var number = 0;
            foreach (var selection in selections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                number++;
                var index = FindCurrentEntry(manifest, selection.ExistingEntry);
                var existing = manifest.Entries[index];
                var key = EntryKey(existing);
                if (!selectedKeys.Add(key))
                    throw new InvalidOperationException("The same backup entry was selected more than once: " +
                                                        existing.Product + " — " + existing.Category);

                ValidatePayloadPath(packageRoot, existing);
                progress?.Report("Checking " + number + "/" + selections.Count + ": " +
                                 existing.Product + " — " + existing.Category);

                var inspectionPath = Path.Combine(inspectionRoot, number.ToString(CultureInfo.InvariantCulture));
                var inspected = await InspectSourceAsync(selection.Source, inspectionPath, existing,
                    cancellationToken);
                var payloadIsHealthy = await ExistingPayloadMatchesManifestAsync(
                    packageRoot, existing, cancellationToken);

                var payloadMatches = SamePayload(existing, inspected);
                if (payloadIsHealthy && payloadMatches)
                {
                    result.SkippedUnchangedSets++;
                    TryDeleteDirectory(inspectionPath);
                    progress?.Report("Unchanged, skipped: " + existing.Product + " — " + existing.Category);
                    continue;
                }

                TryDeleteDirectory(inspectionPath);
                var capturePath = Path.Combine(captureRoot, number.ToString(CultureInfo.InvariantCulture));
                var replacement = await BackupService.CaptureEntryAsync(
                    selection.Source, capturePath, existing.Id, existing.PayloadPath, cancellationToken);
                changed.Add(new ChangedEntry(index, existing, replacement, capturePath));
                result.UpdatedFiles += payloadIsHealthy && !payloadMatches
                    ? CountChangedFiles(existing, replacement) : replacement.FileCount;
            }

            if (changed.Count == 0)
            {
                progress?.Report("Nothing changed. The backup was left untouched.");
                return result;
            }

            progress?.Report("Preparing a safe replacement copy of the backup...");
            await CopyDirectoryAsync(packageRoot, stagingRoot, cancellationToken);

            foreach (var item in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPayload = BackupService.SafeChildPath(stagingRoot, item.Existing.PayloadPath);
                if (File.Exists(stagedPayload))
                    throw new InvalidDataException("A payload path points to a file instead of a folder: " +
                                                   item.Existing.PayloadPath);
                if (Directory.Exists(stagedPayload)) Directory.Delete(stagedPayload, true);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPayload)!);
                Directory.Move(item.CapturePath, stagedPayload);
                manifest.Entries[item.ManifestIndex] = item.Replacement;
            }

            manifest.LastUpdatedUtc = DateTime.UtcNow;
            manifest.LastUpdatedMachine = Environment.MachineName;
            manifest.LastUpdatedUser = Environment.UserName;
            manifest.ToolVersion = UpdateService.CurrentVersionText;
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonSupport.Options), cancellationToken);
            await _restoreService.LoadManifestAsync(stagingRoot, cancellationToken);

            progress?.Report("Applying the update...");
            var previousRoot = UniqueSibling(parent, "." + packageName + ".previous-" + operationId);
            Directory.Move(packageRoot, previousRoot);
            try
            {
                Directory.Move(stagingRoot, packageRoot);
            }
            catch (Exception applyError)
            {
                try
                {
                    if (!Directory.Exists(packageRoot) && Directory.Exists(previousRoot))
                        Directory.Move(previousRoot, packageRoot);
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException(
                        "The update could not be applied and the original folder could not be moved back. " +
                        "The previous backup remains at: " + previousRoot, applyError, restoreError);
                }
                throw;
            }

            result.UpdatedSets = changed.Count;
            try
            {
                Directory.Delete(previousRoot, true);
            }
            catch (Exception ex)
            {
                result.CleanupWarning = "The updated backup is ready, but the temporary previous copy could not " +
                                        "be removed: " + previousRoot + " (" + ex.Message + ")";
            }

            progress?.Report("Backup updated: " + packageRoot);
            return result;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    public static SettingsLocation? MatchSource(
        BackupEntry entry,
        IReadOnlyCollection<SettingsLocation> discovered)
    {
        var exact = discovered.Where(location => SameIdentity(entry, location, includePortablePath: true)).ToList();
        if (exact.Count == 1) return exact[0];
        var fallback = discovered.Where(location => SameIdentity(entry, location, includePortablePath: false)).ToList();
        return fallback.Count == 1 ? fallback[0] : null;
    }

    private static async Task<BackupEntry> InspectSourceAsync(
        SettingsLocation location,
        string inspectionPath,
        BackupEntry existing,
        CancellationToken cancellationToken)
    {
        var entry = NewEntry(location, existing);
        if (location.Kind == SourceKind.Registry)
        {
            Directory.CreateDirectory(inspectionPath);
            var snapshot = RegistryTransfer.Capture(location.SourcePath)
                ?? throw new InvalidOperationException("Could not read " + location.SourcePath);
            var registryFile = Path.Combine(inspectionPath, "registry.json");
            await File.WriteAllTextAsync(registryFile,
                JsonSerializer.Serialize(snapshot, JsonSupport.Options), cancellationToken);
            var info = new FileInfo(registryFile);
            entry.Files.Add(new BackupFile
            {
                RelativePath = "registry.json",
                SizeBytes = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
                Sha256 = await BackupService.HashFileAsync(registryFile, cancellationToken)
            });
        }
        else if (location.Kind == SourceKind.File)
        {
            await AddInspectedFileAsync(entry, location.SourcePath, Path.GetFileName(location.SourcePath),
                cancellationToken);
        }
        else
        {
            foreach (var sourceFile in SafeEnumerateFiles(location.SourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(location.SourcePath, sourceFile);
                if (DiscoveryService.IsExcluded(relative, location.ExcludedPrefixes)) continue;
                await AddInspectedFileAsync(entry, sourceFile, relative, cancellationToken);
            }
        }

        entry.FileCount = entry.Files.Count;
        entry.SizeBytes = entry.Files.Sum(file => file.SizeBytes);
        return entry;
    }

    private static BackupEntry NewEntry(SettingsLocation location, BackupEntry existing) => new()
    {
        Id = existing.Id,
        AppId = location.AppId,
        Product = location.Product,
        SourceVersion = location.Version,
        Category = location.Category,
        Kind = location.Kind,
        OriginalPath = location.SourcePath,
        PortablePath = location.PortablePath,
        PayloadPath = existing.PayloadPath,
        Notes = location.Notes
    };

    private static async Task AddInspectedFileAsync(
        BackupEntry entry,
        string sourceFile,
        string relative,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(sourceFile);
        if (!info.Exists) throw new FileNotFoundException("A settings file disappeared during the update.", sourceFile);
        entry.Files.Add(new BackupFile
        {
            RelativePath = relative.Replace('\\', '/'),
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Sha256 = await BackupService.HashFileAsync(sourceFile, cancellationToken)
        });
    }

    private static async Task<bool> ExistingPayloadMatchesManifestAsync(
        string packageRoot,
        BackupEntry entry,
        CancellationToken cancellationToken)
    {
        var payloadRoot = BackupService.SafeChildPath(packageRoot, entry.PayloadPath);
        foreach (var file in entry.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = BackupService.SafeChildPath(payloadRoot, file.RelativePath);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.SizeBytes) return false;
            if (!string.Equals(await BackupService.HashFileAsync(path, cancellationToken), file.Sha256,
                    StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool SamePayload(BackupEntry left, BackupEntry right)
    {
        if (left.Files.Count != right.Files.Count) return false;
        var rightFiles = right.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var leftFile in left.Files)
        {
            if (!rightFiles.TryGetValue(leftFile.RelativePath, out var rightFile) ||
                leftFile.SizeBytes != rightFile.SizeBytes ||
                !string.Equals(leftFile.Sha256, rightFile.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static int CountChangedFiles(BackupEntry existing, BackupEntry replacement)
    {
        var oldFiles = existing.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var changed = replacement.Files.Count(file =>
            !oldFiles.TryGetValue(file.RelativePath, out var oldFile) ||
            oldFile.SizeBytes != file.SizeBytes ||
            !string.Equals(oldFile.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase));
        var newPaths = replacement.Files.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return changed + existing.Files.Count(file => !newPaths.Contains(file.RelativePath));
    }

    private static int FindCurrentEntry(BackupManifest manifest, BackupEntry selected)
    {
        var matches = manifest.Entries.Select((entry, index) => (entry, index))
            .Where(item => string.Equals(EntryKey(item.entry), EntryKey(selected),
                StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException("The backup changed after it was loaded. Load it again before updating.");
        return matches[0].index;
    }

    private static string EntryKey(BackupEntry entry) => string.Join("\u001f",
        entry.Id, entry.AppId, entry.SourceVersion, entry.Category, entry.Kind, entry.PortablePath);

    private static bool SameIdentity(BackupEntry entry, SettingsLocation location, bool includePortablePath)
    {
        if (!string.Equals(entry.AppId, location.AppId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.SourceVersion, location.Version, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entry.Category, location.Category, StringComparison.OrdinalIgnoreCase) ||
            entry.Kind != location.Kind) return false;
        return !includePortablePath || string.Equals(NormalizePortable(entry.PortablePath),
            NormalizePortable(location.PortablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePortable(string path) => path.Replace('/', '\\').TrimEnd('\\');

    private static string ValidatePackageRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("No backup folder selected.");
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("Backup folder not found: " + fullPath);
        if (string.Equals(fullPath, Path.GetPathRoot(fullPath)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A drive root cannot be updated as a backup package.");
        if (!File.Exists(Path.Combine(fullPath, "manifest.json")))
            throw new FileNotFoundException("The selected folder does not contain manifest.json.");
        return fullPath;
    }

    private static void ValidatePayloadPath(string packageRoot, BackupEntry entry)
    {
        var payload = BackupService.SafeChildPath(packageRoot, entry.PayloadPath);
        if (string.Equals(payload.TrimEnd('\\'), packageRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A backup entry has an unsafe empty payload path.");
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

    private static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationRoot);
        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Backup packages containing directory links cannot be updated safely: " + directory);
                Directory.CreateDirectory(BackupService.SafeChildPath(destinationRoot,
                    Path.GetRelativePath(sourceRoot, directory)));
                pending.Push(directory);
            }
            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "Backup packages containing file links cannot be updated safely: " + file);
                var destination = BackupService.SafeChildPath(destinationRoot, Path.GetRelativePath(sourceRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(target, cancellationToken);
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
            }
        }
    }

    private static string UniqueSibling(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var number = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(parent, name + "-" + number++);
        return candidate;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // Best-effort cleanup only. The original backup remains untouched until the final folder swap.
        }
    }

    private sealed record ChangedEntry(
        int ManifestIndex,
        BackupEntry Existing,
        BackupEntry Replacement,
        string CapturePath);
}
