using System.Text.Json;

namespace GraphicsSettingsMigrator;

internal sealed class UserPreferences
{
    public string BackupDestination { get; set; } = "";
    public bool OverwriteExistingFiles { get; set; } = true;
    public Dictionary<string, bool> BackupSelections { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> RestoreSelections { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GraphicsSettingsMigrator", "preferences.json");

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return new UserPreferences();
            var preferences = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), JsonOptions)
                              ?? new UserPreferences();
            preferences.BackupSelections = new Dictionary<string, bool>(
                preferences.BackupSelections ?? [], StringComparer.OrdinalIgnoreCase);
            preferences.RestoreSelections = new Dictionary<string, bool>(
                preferences.RestoreSelections ?? [], StringComparer.OrdinalIgnoreCase);
            return preferences;
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        catch
        {
            // Remembering UI choices should never prevent backup or restore.
        }
    }
}
