using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphicsSettingsMigrator;

internal sealed class UserOptions
{
    public const int DefaultAutoSelectFolderLimitMb = 500;
    public const int MaximumAutoSelectFolderLimitMb = 1_000_000;

    public int AutoSelectFolderLimitMb { get; set; } = DefaultAutoSelectFolderLimitMb;

    [JsonIgnore]
    public long AutoSelectFolderLimitBytes => AutoSelectFolderLimitMb <= 0
        ? long.MaxValue
        : AutoSelectFolderLimitMb * 1024L * 1024L;

    private static string OptionsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GraphicsSettingsMigrator", "settings.json");

    public static UserOptions Load()
    {
        try
        {
            if (!File.Exists(OptionsPath)) return new UserOptions();
            var options = JsonSerializer.Deserialize<UserOptions>(File.ReadAllText(OptionsPath), JsonSupport.Options)
                          ?? new UserOptions();
            options.AutoSelectFolderLimitMb = Math.Clamp(options.AutoSelectFolderLimitMb, 0,
                MaximumAutoSelectFolderLimitMb);
            return options;
        }
        catch
        {
            return new UserOptions();
        }
    }

    public void Save()
    {
        AutoSelectFolderLimitMb = Math.Clamp(AutoSelectFolderLimitMb, 0,
            MaximumAutoSelectFolderLimitMb);
        var directory = Path.GetDirectoryName(OptionsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = OptionsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonSupport.Options));
        File.Move(temporary, OptionsPath, true);
    }
}
