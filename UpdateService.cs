using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphicsSettingsMigrator;

internal sealed class AvailableUpdate
{
    public required Version Version { get; init; }
    public required string Tag { get; init; }
    public required Uri DownloadUrl { get; init; }
    public required Uri ReleasePage { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

internal sealed class UpdateCheckResult
{
    public required Version CurrentVersion { get; init; }
    public AvailableUpdate? Update { get; init; }
    public bool IsAvailable => Update != null;
}

internal static class UpdateService
{
    private const string Repository = "Ezypoly/GraphicsSettingsMigrator";
    private const string AssetName = "GraphicsSettingsMigrator-win-x64.zip";
    private const string ExecutableName = "GraphicsSettingsMigrator.exe";
    private const string ApplyArgument = "--apply-update";
    private const string CleanupArgument = "--cleanup-update";
    private static readonly string UpdateBase = Path.Combine(
        Path.GetTempPath(), "GraphicsSettingsMigrator", "updates");
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentVersionText => DisplayVersion(CurrentVersion);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream,
                          cancellationToken: cancellationToken)
                      ?? throw new InvalidDataException("GitHub returned an invalid release response.");

        var versionText = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
            throw new InvalidDataException("The latest GitHub release has an invalid version tag: " + release.TagName);

        if (latestVersion <= CurrentVersion)
            return new UpdateCheckResult { CurrentVersion = CurrentVersion };

        var asset = release.Assets.FirstOrDefault(x =>
            x.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The latest release does not contain " + AssetName + ".");
        var expectedHash = ParseDigest(asset.Digest);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            Update = new AvailableUpdate
            {
                Version = latestVersion,
                Tag = release.TagName,
                DownloadUrl = ValidateGitHubUri(asset.DownloadUrl, "/releases/download/"),
                ReleasePage = ValidateGitHubUri(release.HtmlUrl, "/releases/tag/"),
                SizeBytes = asset.Size,
                Sha256 = expectedHash
            }
        };
    }

    public static async Task DownloadAndLaunchAsync(AvailableUpdate update,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        if (!Path.GetFileNameWithoutExtension(currentExecutable)
                .Equals("GraphicsSettingsMigrator", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Self-update is available only in the published portable application, not under dotnet or an IDE.");

        var updateRoot = Path.Combine(UpdateBase, Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(updateRoot, AssetName);
        var payloadRoot = Path.Combine(updateRoot, "payload");
        Directory.CreateDirectory(updateRoot);

        try
        {
            progress?.Report("Downloading " + update.Tag + " from GitHub Releases...");
            using (var response = await Client.GetAsync(update.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken);
            }

            var archiveInfo = new FileInfo(archivePath);
            if (archiveInfo.Length != update.SizeBytes)
                throw new InvalidDataException(
                    $"Downloaded size mismatch. Expected {update.SizeBytes}, received {archiveInfo.Length} bytes.");

            progress?.Report("Verifying the GitHub SHA-256 digest...");
            var actualHash = await HashFileAsync(archivePath, cancellationToken);
            if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded update failed SHA-256 verification.");

            progress?.Report("Preparing the update...");
            Directory.CreateDirectory(payloadRoot);
            ZipFile.ExtractToDirectory(archivePath, payloadRoot);
            var updaterExecutable = Directory.EnumerateFiles(payloadRoot, ExecutableName,
                    SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException("The update archive does not contain " + ExecutableName + ".");

            var downloadedVersion = FileVersionInfo.GetVersionInfo(updaterExecutable).FileVersion;
            if (!Version.TryParse(downloadedVersion, out var payloadVersion) ||
                !SameReleaseVersion(payloadVersion, update.Version))
                throw new InvalidDataException(
                    $"Update executable version mismatch. Expected {DisplayVersion(update.Version)}, received {downloadedVersion}.");

            var needsElevation = !CanWriteToDirectory(Path.GetDirectoryName(currentExecutable)!);
            var startInfo = new ProcessStartInfo(updaterExecutable)
            {
                UseShellExecute = needsElevation,
                Verb = needsElevation ? "runas" : ""
            };
            startInfo.ArgumentList.Add(ApplyArgument);
            startInfo.ArgumentList.Add(currentExecutable);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(updateRoot);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the update helper.");
        }
        catch
        {
            TryDeleteDirectory(updateRoot);
            throw;
        }
    }

    public static bool TryApplyUpdate(string[] args)
    {
        if (args.Length != 4 || !args[0].Equals(ApplyArgument, StringComparison.Ordinal)) return false;
        try
        {
            var targetExecutable = Path.GetFullPath(args[1]);
            if (!int.TryParse(args[2], out var parentProcessId))
                throw new InvalidDataException("The updater received an invalid process identifier.");
            var updateRoot = ValidateUpdateRoot(args[3]);
            var payloadRoot = Path.Combine(updateRoot, "payload");
            var payloadExecutable = Directory.EnumerateFiles(payloadRoot, ExecutableName,
                    SearchOption.AllDirectories).SingleOrDefault()
                ?? throw new InvalidDataException("The prepared update payload is incomplete.");

            if (!WaitForProcess(parentProcessId))
                throw new TimeoutException("The running application did not close within 60 seconds.");
            CopyPayload(payloadRoot, targetExecutable, payloadExecutable);

            var startInfo = new ProcessStartInfo(targetExecutable) { UseShellExecute = true };
            startInfo.ArgumentList.Add(CleanupArgument);
            startInfo.ArgumentList.Add(updateRoot);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not restart the updated application.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("The update could not be installed.\n\n" + ex.Message,
                "Graphics Settings Migrator updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return true;
    }

    public static void ScheduleCleanup(string[] args)
    {
        if (args.Length != 3 || !args[0].Equals(CleanupArgument, StringComparison.Ordinal)) return;
        if (!int.TryParse(args[2], out var updaterProcessId)) return;
        string updateRoot;
        try { updateRoot = ValidateUpdateRoot(args[1]); }
        catch { return; }

        _ = Task.Run(() =>
        {
            if (!WaitForProcess(updaterProcessId)) return;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (TryDeleteDirectory(updateRoot)) return;
                Thread.Sleep(500);
            }
        });
    }

    private static void CopyPayload(string payloadRoot, string targetExecutable, string payloadExecutable)
    {
        var targetDirectory = Path.GetDirectoryName(targetExecutable)
            ?? throw new InvalidDataException("The target application directory is invalid.");
        Directory.CreateDirectory(targetDirectory);
        var sources = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .OrderBy(x => x.Equals(payloadExecutable, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        foreach (var source in sources)
        {
            var relative = Path.GetRelativePath(payloadRoot, source);
            var destination = source.Equals(payloadExecutable, StringComparison.OrdinalIgnoreCase)
                ? targetExecutable
                : SafeChildPath(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            ReplaceWithRetry(source, destination);
        }
    }

    private static void ReplaceWithRetry(string source, string destination)
    {
        Exception? lastError = null;
        var temporary = destination + ".gsm-new-" + Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Copy(source, temporary, true);
                File.Move(temporary, destination, true);
                return;
            }
            catch (IOException ex) { lastError = ex; }
            catch (UnauthorizedAccessException ex) { lastError = ex; }
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch { }
            Thread.Sleep(250);
        }
        try
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        catch { }
        throw new IOException("Could not replace " + destination, lastError);
    }

    private static bool WaitForProcess(int processId)
    {
        if (processId <= 0 || processId == Environment.ProcessId) return true;
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit(60_000);

        }
        catch (ArgumentException) { return true; }
    }

    private static Uri ValidateGitHubUri(string value, string requiredPathPart)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains("/Ezypoly/GraphicsSettingsMigrator/", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains(requiredPathPart, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned an unexpected release URL.");
        return uri;
    }

    private static bool SameReleaseVersion(Version left, Version right) =>
        left.Major == right.Major && left.Minor == right.Minor &&
        Math.Max(left.Build, 0) == Math.Max(right.Build, 0);

    private static string ValidateUpdateRoot(string path)
    {
        var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var allowed = Path.GetFullPath(UpdateBase).TrimEnd(Path.DirectorySeparatorChar) +
                      Path.DirectorySeparatorChar;
        if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root))
            throw new InvalidDataException("The updater temporary directory is invalid.");
        return root;
    }

    private static string SafeChildPath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe update path: " + relative);
        return fullPath;
    }

    private static bool CanWriteToDirectory(string directory)
    {
        var probe = Path.Combine(directory, ".gsm-update-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string ParseDigest(string digest)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub did not provide a SHA-256 digest for the release asset.");
        var hash = digest[prefix.Length..];
        try
        {
            if (Convert.FromHexString(hash).Length != 32) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new InvalidDataException("GitHub returned an invalid release asset digest.");
        }
        return hash;
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            return !Directory.Exists(path);
        }
        catch { return false; }
    }

    private static string DisplayVersion(Version version) =>
        version.Build >= 0 ? $"{version.Major}.{version.Minor}.{version.Build}" : version.ToString();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "GraphicsSettingsMigrator", CurrentVersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; init; } = "";

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string Digest { get; init; } = "";
    }
}
