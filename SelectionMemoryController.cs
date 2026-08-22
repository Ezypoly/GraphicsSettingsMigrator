using System.Runtime.CompilerServices;

namespace GraphicsSettingsMigrator;

internal static class SelectionMemoryBootstrap
{
    private static readonly ConditionalWeakTable<MainForm, SelectionMemoryController> Controllers = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += AttachToOpenForms;
    }

    private static void AttachToOpenForms(object? sender, EventArgs eventArgs)
    {
        foreach (var form in Application.OpenForms.OfType<MainForm>())
            if (!Controllers.TryGetValue(form, out _))
                Controllers.Add(form, new SelectionMemoryController(form));
    }
}

internal sealed class SelectionMemoryController
{
    private readonly UserPreferencesStore _store = new();
    private readonly UserPreferences _preferences;
    private readonly HashSet<DataGridViewRow> _initializedRows = [];
    private bool _applying;

    public SelectionMemoryController(MainForm form)
    {
        _preferences = _store.Load();
        foreach (var grid in Descendants<DataGridView>(form).Where(IsSettingsGrid))
            Attach(grid);
        form.FormClosing += (_, _) => SaveAll(form);
    }

    private void Attach(DataGridView grid)
    {
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell?.OwningColumn.Name == "Selected")
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, eventArgs) =>
        {
            if (_applying || eventArgs.RowIndex < 0 ||
                grid.Columns[eventArgs.ColumnIndex].Name != "Selected") return;
            SaveRow(grid.Rows[eventArgs.RowIndex], IsRestoreGrid(grid));
        };
        grid.RowsAdded += (_, _) => grid.BeginInvoke(ApplyNewRows, grid);
        ApplyNewRows(grid);
    }

    private void ApplyNewRows(DataGridView grid)
    {
        _applying = true;
        try
        {
            var savedSelections = IsRestoreGrid(grid)
                ? _preferences.RestoreSelections
                : _preferences.BackupSelections;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!_initializedRows.Add(row)) continue;
                var key = SelectionKey(row.Tag);
                if (key != null && savedSelections.TryGetValue(key, out var selected))
                    row.Cells["Selected"].Value = selected;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void SaveAll(Control root)
    {
        foreach (var grid in Descendants<DataGridView>(root).Where(IsSettingsGrid))
            foreach (DataGridViewRow row in grid.Rows)
                SaveRow(row, IsRestoreGrid(grid), saveFile: false);
        SavePreferences();
    }

    private void SaveRow(DataGridViewRow row, bool restore, bool saveFile = true)
    {
        var key = SelectionKey(row.Tag);
        if (key == null) return;
        var selected = row.Cells["Selected"].Value is true ||
                       bool.TryParse(Convert.ToString(row.Cells["Selected"].Value), out var value) && value;
        var selections = restore ? _preferences.RestoreSelections : _preferences.BackupSelections;
        selections[key] = selected;
        if (saveFile) SavePreferences();
    }

    private void SavePreferences()
    {
        var latest = _store.Load();
        latest.BackupSelections = new Dictionary<string, bool>(
            _preferences.BackupSelections, StringComparer.OrdinalIgnoreCase);
        latest.RestoreSelections = new Dictionary<string, bool>(
            _preferences.RestoreSelections, StringComparer.OrdinalIgnoreCase);
        _store.Save(latest);
    }

    private static string? SelectionKey(object? item) => item switch
    {
        SettingsLocation location => BuildKey(location.AppId, location.Version, location.Category,
            location.PortablePath, location.Kind),
        BackupEntry entry => BuildKey(entry.AppId, entry.SourceVersion, entry.Category,
            entry.PortablePath, entry.Kind),
        BackupUpdateItem { Source: { } source } => BuildKey(source.AppId, source.Version, source.Category,
            source.PortablePath, source.Kind),
        BackupUpdateItem { ExistingEntry: { } entry } => BuildKey(entry.AppId, entry.SourceVersion, entry.Category,
            entry.PortablePath, entry.Kind),
        _ => null
    };

    private static string BuildKey(string appId, string version, string category,
        string portablePath, SourceKind kind) =>
        string.Join("\u001f", appId, version, category, portablePath, kind.ToString());

    private static bool IsSettingsGrid(DataGridView grid) =>
        grid.Columns.Contains("Selected") &&
        (grid.Columns.Contains("Path") || grid.Columns.Contains("TargetPath"));

    private static bool IsRestoreGrid(DataGridView grid) => grid.Columns.Contains("TargetPath");

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
