using System.Globalization;
using System.Runtime.CompilerServices;

namespace GraphicsSettingsMigrator;

internal static class BackupUpdateUiBootstrap
{
    private static readonly ConditionalWeakTable<MainForm, BackupUpdateUiController> Controllers = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += AttachToOpenForms;
    }

    private static void AttachToOpenForms(object? sender, EventArgs eventArgs)
    {
        foreach (var form in Application.OpenForms.OfType<MainForm>())
            if (!Controllers.TryGetValue(form, out _))
                Controllers.Add(form, new BackupUpdateUiController(form));
    }
}

internal sealed class BackupUpdateUiController
{
    private readonly MainForm _form;
    private readonly DiscoveryService _discovery = new();
    private readonly RestoreService _restoreService = new();
    private readonly BackupUpdateService _updateService = new();
    private readonly UserOptions _options = UserOptions.Load();
    private readonly TextBox _path = new() { Width = 470 };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5F)
    };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoGenerateColumns = false, BorderStyle = BorderStyle.Fixed3D
    };
    private readonly Button _browse = new() { Text = "Backup...", AutoSize = true };
    private readonly Button _load = new() { Text = "Load backup", AutoSize = true };
    private readonly Button _toggle = new() { Text = "Select / clear all", AutoSize = true };
    private readonly Button _update = new() { Text = "Update selected", AutoSize = true };
    private BackupManifest? _manifest;
    private TabControl? _tabs;

    public BackupUpdateUiController(MainForm form)
    {
        _form = form;
        _tabs = Descendants<TabControl>(form).FirstOrDefault();
        if (_tabs == null) return;
        ConfigureGrid();
        var page = BuildTab();
        _tabs.TabPages.Insert(Math.Min(1, _tabs.TabPages.Count), page);
        DarkTheme.Apply(page);
        _tabs.Invalidate();

        _browse.Click += async (_, _) => await BrowseAsync();
        _load.Click += async (_, _) => await LoadAsync();
        _toggle.Click += (_, _) => ToggleAll();
        _update.Click += async (_, _) => await UpdateAsync();
    }

    private TabPage BuildTab()
    {
        var page = new TabPage("Update backup");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Choose an existing backup. The left column shows its contents and availability on this PC; " +
                   "use the right column to choose which sets to refresh. Unchanged sets are skipped by SHA-256 " +
                   "comparison. Unchecked or unavailable contents remain untouched."
        }, 0, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 6, 0, 6)
        };
        actions.Controls.Add(_path);
        actions.Controls.Add(_browse);
        actions.Controls.Add(_load);
        actions.Controls.Add(_toggle);
        actions.Controls.Add(_update);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_log, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Contents", HeaderText = "Existing backup contents (read-only)", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 500
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected", HeaderText = "Update", Width = 85
        });
        ConfigureMultiRowSelection();
    }

    private async Task BrowseAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select an existing backup folder containing manifest.json",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_path.Text)) dialog.InitialDirectory = _path.Text;
        if (dialog.ShowDialog(_form) != DialogResult.OK) return;
        _path.Text = dialog.SelectedPath;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var package = _path.Text.Trim();
        if (string.IsNullOrWhiteSpace(package)) return;
        SetBusy(true);
        _log.Clear();
        try
        {
            Log("Loading backup and scanning this PC...");
            _manifest = await _restoreService.LoadManifestAsync(package);
            var discovered = await Task.Run(_discovery.DiscoverExisting);
            _grid.Rows.Clear();
            var available = 0;
            foreach (var entry in _manifest.Entries)
            {
                var source = BackupUpdateService.MatchSource(entry, discovered);
                var item = new BackupUpdateItem { ExistingEntry = entry, Source = source };
                var selected = source != null && ShouldAutoSelect(source);
                var contents = FormatContents(entry, source);
                var rowIndex = _grid.Rows.Add(contents, selected);
                var row = _grid.Rows[rowIndex];
                row.Tag = item;
                if (source == null)
                {
                    row.Cells["Selected"].ReadOnly = true;
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(165, 168, 175);
                }
                else
                {
                    available++;
                    if (!selected) row.DefaultCellStyle.ForeColor = Color.FromArgb(165, 168, 175);
                }
            }
            Log("Backup contents: " + _manifest.Entries.Count + ". Available to update: " + available + ".");
            Log("Cache and folders above " + _options.AutoSelectFolderLimitMb +
                " MB require manual selection (0 means unlimited).");
        }
        catch (Exception ex)
        {
            _manifest = null;
            _grid.Rows.Clear();
            ShowError(ex);
            Log(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateAsync()
    {
        _grid.EndEdit();
        if (_manifest == null)
        {
            MessageBox.Show(_form, "Load an existing backup first.", _form.Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selections = _grid.Rows.Cast<DataGridViewRow>()
            .Where(IsSelected)
            .Select(row => row.Tag)
            .OfType<BackupUpdateItem>()
            .Where(item => item.Source != null)
            .Select(item => new BackupUpdateSelection
            {
                ExistingEntry = item.ExistingEntry,
                Source = item.Source!
            }).ToList();
        if (selections.Count == 0)
        {
            MessageBox.Show(_form, "Select at least one available backup content item.", _form.Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var running = RestoreService.FindRunningGraphicsApps();
        if (running.Count > 0)
        {
            var runningAnswer = MessageBox.Show(_form,
                "These applications are currently running:\n\n" + string.Join("\n", running) +
                "\n\nClose them first for the most up-to-date backup. Continue anyway?",
                "Applications are running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (runningAnswer != DialogResult.Yes) return;
        }

        var answer = MessageBox.Show(_form,
            "Update " + selections.Count + " selected settings set(s) in this backup?\n\n" +
            "Only changed sets will be replaced. SHA-256-identical sets will be skipped. " +
            "Unchecked and unavailable contents will remain untouched.\n\n" + _path.Text.Trim(),
            "Confirm backup update", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(Log);
            var result = await _updateService.UpdateAsync(_path.Text.Trim(), selections, progress);
            var message = result.UpdatedSets == 0
                ? "No changes were found. The backup was left untouched.\n\nUnchanged sets skipped: " +
                  result.SkippedUnchangedSets
                : "Backup updated.\n\nUpdated sets: " + result.UpdatedSets +
                  "\nChanged/removed files: " + result.UpdatedFiles +
                  "\nUnchanged sets skipped: " + result.SkippedUnchangedSets;
            if (!string.IsNullOrWhiteSpace(result.CleanupWarning))
                message += "\n\nWarning: " + result.CleanupWarning;
            MessageBox.Show(_form, message, _form.Text, MessageBoxButtons.OK,
                string.IsNullOrWhiteSpace(result.CleanupWarning)
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Log(ex.ToString());
        }
        finally
        {
            SetBusy(false);
        }

        await LoadAsync();
    }

    private void ToggleAll()
    {
        _grid.EndEdit();
        var eligible = _grid.Rows.Cast<DataGridViewRow>().Where(IsAutomaticallySelectable).ToList();
        var select = eligible.Any(row => !IsSelected(row));
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (select)
            {
                if (eligible.Contains(row)) row.Cells["Selected"].Value = true;
            }
            else if (!row.Cells["Selected"].ReadOnly)
                row.Cells["Selected"].Value = false;
        }
        _grid.InvalidateColumn(_grid.Columns["Selected"].Index);
    }

    private bool IsAutomaticallySelectable(DataGridViewRow row)
    {
        if (row.Cells["Selected"].ReadOnly || row.Tag is not BackupUpdateItem { Source: { } source }) return false;
        return !IsCache(source.Category, source.Notes) &&
               !(source.Kind == SourceKind.Directory && source.SizeBytes > _options.AutoSelectFolderLimitBytes);
    }

    private bool ShouldAutoSelect(SettingsLocation source) => source.Recommended &&
        !IsCache(source.Category, source.Notes) &&
        !(source.Kind == SourceKind.Directory && source.SizeBytes > _options.AutoSelectFolderLimitBytes);

    private static string FormatContents(BackupEntry entry, SettingsLocation? source)
    {
        var stored = entry.Kind == SourceKind.Registry
            ? "registry"
            : entry.FileCount.ToString("N0", CultureInfo.CurrentCulture) + " files, " + FormatBytes(entry.SizeBytes);
        var availability = source == null
            ? "not found on this PC"
            : "available now: " + (source.Kind == SourceKind.Registry
                ? "registry"
                : source.FileCount.ToString("N0", CultureInfo.CurrentCulture) + " files, " +
                  FormatBytes(source.SizeBytes));
        return entry.Product + " " + entry.SourceVersion + " — " + entry.Category +
               "  |  stored: " + stored + "  |  " + availability;
    }

    private void ConfigureMultiRowSelection()
    {
        List<DataGridViewRow>? clickRows = null;
        var applying = false;
        var selectedColumn = _grid.Columns["Selected"].Index;
        _grid.CellMouseDown += (_, e) =>
        {
            clickRows = null;
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex != selectedColumn) return;
            var clicked = _grid.Rows[e.RowIndex];
            var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
            if (selectedRows.Count > 1 && selectedRows.Contains(clicked)) clickRows = selectedRows;
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell?.ColumnIndex == selectedColumn)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (applying || e.RowIndex < 0 || e.ColumnIndex != selectedColumn) return;
            var rows = clickRows;
            clickRows = null;
            if (rows is not { Count: > 1 } || !rows.Contains(_grid.Rows[e.RowIndex])) return;
            var value = IsSelected(_grid.Rows[e.RowIndex]);
            applying = true;
            try
            {
                foreach (var row in rows.Where(row => !row.Cells["Selected"].ReadOnly))
                    row.Cells["Selected"].Value = value;
            }
            finally
            {
                applying = false;
            }
            _grid.InvalidateColumn(selectedColumn);
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || _grid.SelectedRows.Count == 0) return;
            var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Where(row => !row.Cells["Selected"].ReadOnly).ToList();
            var value = rows.Any(row => !IsSelected(row));
            foreach (var row in rows) row.Cells["Selected"].Value = value;
            _grid.InvalidateColumn(selectedColumn);
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }

    private void SetBusy(bool busy)
    {
        _form.UseWaitCursor = busy;
        _browse.Enabled = !busy;
        _load.Enabled = !busy;
        _toggle.Enabled = !busy;
        _update.Enabled = !busy;
    }

    private void Log(string message) =>
        _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);

    private void ShowError(Exception ex) =>
        MessageBox.Show(_form, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static bool IsCache(string category, string notes) =>
        category.Contains("cache", StringComparison.OrdinalIgnoreCase) ||
        notes.Contains("cache", StringComparison.OrdinalIgnoreCase);

    private static bool IsSelected(DataGridViewRow row) =>
        row.Cells["Selected"].Value is true ||
        bool.TryParse(Convert.ToString(row.Cells["Selected"].Value), out var selected) && selected;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
