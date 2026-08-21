using Microsoft.Win32;

namespace GraphicsSettingsMigrator;

public enum SourceKind
{
    Directory,
    File,
    Registry
}

public sealed class SettingsLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppId { get; set; } = "";
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string Category { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string SourcePath { get; set; } = "";
    public string PortablePath { get; set; } = "";
    public bool Recommended { get; set; } = true;
    public string Notes { get; set; } = "";
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public List<string> ExcludedPrefixes { get; set; } = [];
}

public sealed class TargetLocation
{
    public string AppId { get; set; } = "";
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string Category { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string TargetPath { get; set; } = "";
    public bool Exists { get; set; }
}

public sealed class BackupManifest
{
    public int FormatVersion { get; set; } = 1;
    public string ToolVersion { get; set; } = "1.0";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string SourceMachine { get; set; } = Environment.MachineName;
    public string SourceUser { get; set; } = Environment.UserName;
    public List<BackupEntry> Entries { get; set; } = [];
}

public sealed class BackupEntry
{
    public string Id { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Product { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string Category { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string OriginalPath { get; set; } = "";
    public string PortablePath { get; set; } = "";
    public string PayloadPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public List<BackupFile> Files { get; set; } = [];
}

public sealed class BackupFile
{
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class RegistrySnapshot
{
    public string KeyPath { get; set; } = "";
    public List<RegistryValueSnapshot> Values { get; set; } = [];
    public List<RegistrySnapshot> SubKeys { get; set; } = [];
}

public sealed class RegistryValueSnapshot
{
    public string Name { get; set; } = "";
    public RegistryValueKind Kind { get; set; }
    public List<string> Data { get; set; } = [];
}

public sealed class RestoreSelection
{
    public required BackupEntry Entry { get; init; }
    public required string TargetPath { get; init; }
}

public sealed class RestorePreview
{
    public int FilesToCopy { get; set; }
    public int ExistingFiles { get; set; }
    public int MissingPayloadFiles { get; set; }
    public long BytesToCopy { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class RestoreResult
{
    public int CopiedFiles { get; set; }
    public int SkippedFiles { get; set; }
    public string RollbackPath { get; set; } = "";
}
