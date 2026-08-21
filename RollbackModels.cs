namespace GraphicsSettingsMigrator;

public sealed class RollbackManifest
{
    public int FormatVersion { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public DateTime? RevertedUtc { get; set; }
    public string SourcePackage { get; set; } = "";
    public List<RollbackEntry> Entries { get; set; } = [];
}

public sealed class RollbackEntry
{
    public string AppId { get; set; } = "";
    public string Product { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string Category { get; set; } = "";
    public SourceKind Kind { get; set; }
    public string TargetPath { get; set; } = "";
    public string BackupDirectory { get; set; } = "";
    public bool RegistryExistedBefore { get; set; }
    public List<RollbackFile> Files { get; set; } = [];
}

public sealed class RollbackFile
{
    public string RelativePath { get; set; } = "";
    public bool ExistedBefore { get; set; }
    public DateTime? PreviousLastWriteUtc { get; set; }
    public string AppliedSha256 { get; set; } = "";
}

public sealed class RollbackPackage
{
    public string FolderPath { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public RollbackManifest? Manifest { get; init; }
    public bool CanRevert => Manifest != null;
    public bool WasReverted => Manifest?.RevertedUtc != null;

    public override string ToString()
    {
        var state = !CanRevert ? "legacy / manual only" : WasReverted ? "already reverted" : "ready";
        var sets = Manifest?.Entries.Count ?? 0;
        return CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
               "  |  " + state + "  |  " + sets + " settings sets  |  " + FolderPath;
    }
}

public sealed class RollbackRevertResult
{
    public int RestoredFiles { get; set; }
    public int RemovedFiles { get; set; }
    public int RestoredRegistryKeys { get; set; }
    public int SkippedChangedFiles { get; set; }
}
