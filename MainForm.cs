using System.Globalization;

namespace GraphicsSettingsMigrator;

public sealed class MainForm : Form
{
    private readonly DiscoveryService _discovery = new();
    private readonly BackupService _backupService = new();
    private readonly RestoreService _restoreService = new();
    private readonly RemovalService _removalService = new();
    private readonly UserOptions _options = UserOptions.Load();
    private readonly UserPreferencesStore _preferencesStore = new();
    private readonly DataGridView _backupGrid = CreateGrid();
    private readonly DataGridView _restoreGrid = CreateGrid();
    private readonly TextBox _backupDestination = new();
    private readonly TextBox _packagePath = new();
    private readonly TextBox _backupLog = CreateLog();
    private readonly TextBox _restoreLog = CreateLog();
    private readonly Button _scanButton = new() { Text = "Scan", AutoSize = true };
    private readonly Button _backupButton = new() { Text = "Save selected", AutoSize = true };
    private readonly Button _loadButton = new() { Text = "Open", AutoSize = true };
    private readonly Button _previewButton = new() { Text = "Preview", AutoSize = true };
    private readonly Button _restoreButton = new() { Text = "Restore", AutoSize = true };
    private readonly Button _updateButton = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button _toggleBackupButton = new() { Text = "Select / clear all", AutoSize = true };
    private readonly Button _toggleRestoreButton = new() { Text = "Select / clear all", AutoSize = true };
    private readonly Button _removeButton = new() { Text = "Remove selected...", AutoSize = true };
    private readonly NumericUpDown _autoSelectLimit = new()
    {
        Minimum = 0,
        Maximum = UserOptions.MaximumAutoSelectFolderLimitMb,
        Increment = 100,
        ThousandsSeparator = true,
        Width = 90
    };
    private readonly CheckBox _overwrite = new()
    {
        Text = "Overwrite existing files (with rollback copy)",
        Checked = true,
        AutoSize = true
    };
    private BackupManifest? _loadedManifest;

    public MainForm()
    {
        Text = "Graphics Settings Migrator " + UpdateService.CurrentVersionText;
        Width = 1220;
        Height = 790;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        var preferences = _preferencesStore.Load();
        _backupDestination.Text = string.IsNullOrWhiteSpace(preferences.BackupDestination)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GraphicsSettingsBackups")
            : preferences.BackupDestination;
        _packagePath.Text = preferences.RestorePackagePath;
        _overwrite.Checked = preferences.OverwriteExistingFiles;
        _autoSelectLimit.Value = _options.AutoSelectFolderLimitMb;
        ConfigureBackupGrid();
        ConfigureRestoreGrid();
        ConfigureMultiRowSelection(_backupGrid);
        ConfigureMultiRowSelection(_restoreGrid);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildBackupTab());
        tabs.TabPages.Add(BuildRestoreTab());
        Controls.Add(tabs);
        DarkTheme.Apply(this, tabs);
        _scanButton.Click += async (_, _) => await ScanAsync();
        _backupButton.Click += async (_, _) => await BackupAsync();
        _loadButton.Click += async (_, _) => await LoadPackageAsync();
        _previewButton.Click += (_, _) => ShowPreview();
        _restoreButton.Click += async (_, _) => await RestoreAsync();
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _toggleBackupButton.Click += (_, _) => ToggleAll(_backupGrid);
        _removeButton.Click += async (_, _) => await RemoveSelectedAsync();
        _toggleRestoreButton.Click += (_, _) => ToggleAll(_restoreGrid);
        Shown += async (_, _) => await ScanAsync();
        _autoSelectLimit.ValueChanged += (_, _) => AutoSelectLimitChanged();
        _backupDestination.TextChanged += (_, _) => SavePathPreferences();
        _packagePath.TextChanged += (_, _) => SavePathPreferences();
        _overwrite.CheckedChanged += (_, _) => SavePathPreferences();
        FormClosing += (_, _) => SavePathPreferences();
    }

    private TabPage BuildBackupTab()
    {
        var page = new TabPage("Save");
        var layout = NewLayout();
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Find existing settings, select the required sets, and save a portable settings copy. " +
                   "ZBrush QuickSave and Temp data are not included. Large folders above the saved auto-select " +
                   "limit stay visible but unchecked. Use Ctrl/Shift to select rows; press Space to toggle " +
                   "their checkboxes."
        };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 6, 0, 6)
        };
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseInto(_backupDestination, "Choose where to save the settings copy");
        _backupDestination.Width = 390;
        actions.Controls.Add(_removeButton);
        actions.Controls.Add(_toggleBackupButton);
        actions.Controls.Add(_scanButton);
        actions.Controls.Add(new Label { Text = "  Save to:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        actions.Controls.Add(_backupDestination);
        actions.Controls.Add(browse);
        actions.Controls.Add(new Label { Text = "  Auto-select folders up to:", AutoSize = true,
            Padding = new Padding(8, 7, 0, 0) });
        actions.Controls.Add(_autoSelectLimit);
        actions.Controls.Add(new Label { Text = "MB (0 = unlimited)", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        actions.Controls.Add(_backupButton);
        actions.Controls.Add(new Label { Text = "  Version " + UpdateService.CurrentVersionText,
            AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        actions.Controls.Add(_updateButton);
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_backupGrid, 0, 2);
        layout.Controls.Add(_backupLog, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildRestoreTab()
    {
        var page = new TabPage("Open / restore");
        var layout = NewLayout();
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Select a folder containing manifest.json. Targets are matched to installed versions automatically; " +
                   "you can edit any target path manually. Restore merges files and never deletes extra target files. " +
                   "Use Ctrl/Shift to select rows; press Space to toggle their checkboxes."
        };
        var top = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 6, 0, 6)
        };
        _packagePath.Width = 420;
        var browse = new Button { Text = "Open...", AutoSize = true };
        browse.Click += async (_, _) => { if (BrowseInto(_packagePath, "Select a saved settings folder")) await LoadPackageAsync(); };
        var autoMap = new Button { Text = "Refresh targets", AutoSize = true };
        autoMap.Click += (_, _) => AutoMapTargets();
        var chooseTarget = new Button { Text = "Target folder…", AutoSize = true };
        chooseTarget.Click += (_, _) => ChooseTargetForCurrentRow();
        top.Controls.Add(_packagePath);
        top.Controls.Add(browse);
        top.Controls.Add(_toggleRestoreButton);
        top.Controls.Add(_loadButton);
        top.Controls.Add(autoMap);
        top.Controls.Add(chooseTarget);
        top.Controls.Add(_previewButton);
        top.Controls.Add(_restoreButton);
        top.Controls.Add(_overwrite);
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(top, 0, 1);
        layout.Controls.Add(_restoreGrid, 0, 2);
        layout.Controls.Add(_restoreLog, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private static TableLayoutPanel NewLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        return layout;
    }

    private void ConfigureBackupGrid()
    {
        _backupGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Save", Width = 75 });
        _backupGrid.Columns.Add(TextColumn("Product", "Application", 180, true));
        _backupGrid.Columns.Add(TextColumn("Version", "Version", 75, true));
        _backupGrid.Columns.Add(TextColumn("Category", "Settings set", 185, true));
        _backupGrid.Columns.Add(TextColumn("Size", "Size", 80, true));
        _backupGrid.Columns.Add(TextColumn("Files", "Files", 60, true));
        _backupGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Path", HeaderText = "Path", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260, ReadOnly = true
        });
        _backupGrid.Columns.Add(TextColumn("Notes", "Notes", 250, true));
    }

    private void ConfigureRestoreGrid()
    {
        _restoreGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Restore", Width = 70 });
        _restoreGrid.Columns.Add(TextColumn("Product", "Application", 180, true));
        _restoreGrid.Columns.Add(TextColumn("Version", "From version", 75, true));
        _restoreGrid.Columns.Add(TextColumn("Category", "Settings set", 185, true));
        _restoreGrid.Columns.Add(TextColumn("Size", "Size", 80, true));
        _restoreGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "TargetPath", HeaderText = "Target path (editable)",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 350
        });
        _restoreGrid.Columns.Add(TextColumn("TargetStatus", "Target", 90, true));
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width, bool readOnly) =>
        new() { Name = name, HeaderText = header, Width = width, ReadOnly = readOnly };

    private async Task ScanAsync()
    {
        SetBusy(true);
        _backupLog.Clear();
        Log(_backupLog, "Scanning for settings…");
        try
        {
            var locations = await Task.Run(_discovery.DiscoverExisting);
            _backupGrid.Rows.Clear();
            foreach (var location in locations)
            {
                var autoSelected = ShouldAutoSelect(location);
                var rowIndex = _backupGrid.Rows.Add(autoSelected, location.Product, location.Version,
                    location.Category, FormatBytes(location.SizeBytes),
                    location.Kind == SourceKind.Registry ? "registry" : location.FileCount,
                    location.SourcePath, DisplayNotes(location));
                var row = _backupGrid.Rows[rowIndex];
                row.Tag = location;
                UpdateBackupRowAppearance(row, location);
            }
            Log(_backupLog, "Settings sets found: " + locations.Count +
                            ". Optional, cache-containing, or size-limited sets are shown in gray.");
            Log(_backupLog, "Supported application catalog: " + (ExtendedDiscovery.SupportedProducts.Length + 12) + " products.");
            Log(_backupLog, "Folder auto-selection limit: " + _options.AutoSelectFolderLimitMb +
                            " MB (0 means unlimited).");
        }
        catch (Exception ex) { ShowError(ex); Log(_backupLog, ex.Message); }
        finally { SetBusy(false); }
    }

    private async Task BackupAsync()
    {
        _backupGrid.EndEdit();
        var selected = _backupGrid.Rows.Cast<DataGridViewRow>().Where(IsSelected)
            .Select(x => x.Tag).OfType<SettingsLocation>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one settings set.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var running = RestoreService.FindRunningGraphicsApps();
        if (running.Count > 0)
        {
            var answer = MessageBox.Show(this, "These applications are currently running:\n\n" + string.Join("\n", running) +
                "\n\nClose them first for the most up-to-date saved copy. Continue anyway?",
                "Applications are running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => Log(_backupLog, message));
            var package = await _backupService.CreateBackupAsync(
                selected, _backupDestination.Text.Trim(), progress);
            MessageBox.Show(this, "Settings saved:\n\n" + package,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); Log(_backupLog, ex.ToString()); }
        finally { SetBusy(false); }
    }

    private async Task RemoveSelectedAsync()
    {
        var selected = _backupGrid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.Tag)
            .OfType<SettingsLocation>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Highlight at least one settings row to remove.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var running = RestoreService.FindRunningGraphicsApps();
        if (running.Count > 0)
        {
            MessageBox.Show(this, "Close these applications before removing settings:\n\n" +
                string.Join("\n", running) +
                "\n\nRemoval is blocked while a graphics application is running.",
                "Close applications first", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var summary = string.Join("\n", selected.Take(12)
            .Select(x => "• " + x.Product + " " + x.Version + " — " + x.Category));
        if (selected.Count > 12) summary += "\n• ...and " + (selected.Count - 12) + " more";
        var recoveryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GraphicsSettingsMigrator Removed Settings");
        var answer = MessageBox.Show(this,
            "Permanently remove " + selected.Count + " highlighted settings set(s) from this PC?\n\n" +
            summary + "\n\nA recovery copy will be saved first in:\n" + recoveryRoot +
            "\n\nOnly files verified against that copy will be deleted. Excluded projects, scenes, " +
            "and caches remain untouched unless their own settings row is explicitly highlighted.",
            "Confirm settings removal", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var refresh = false;
        SetBusy(true);
        _backupLog.Clear();
        try
        {
            var progress = new Progress<string>(message => Log(_backupLog, message));
            var result = await _removalService.RemoveAsync(selected, progress);
            refresh = true;
            var message = "Removal finished.\n\nFiles removed: " + result.RemovedFiles +
                          "\nRegistry keys removed: " + result.RemovedRegistryKeys +
                          "\nRecovery copy:\n" + result.RecoveryBackupPath;
            if (result.Failures.Count > 0)
                message += "\n\nNot removed (" + result.Failures.Count + "):\n" +
                           string.Join("\n", result.Failures.Take(10));
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK,
                result.Failures.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Log(_backupLog, ex.ToString());
        }
        finally { SetBusy(false); }

        if (refresh) await ScanAsync();
    }

    private async Task LoadPackageAsync()
    {
        var package = _packagePath.Text.Trim();
        if (string.IsNullOrWhiteSpace(package)) return;
        SetBusy(true);
        _restoreLog.Clear();
        try
        {
            _loadedManifest = await _restoreService.LoadManifestAsync(package);
            _restoreGrid.Rows.Clear();
            foreach (var entry in _loadedManifest.Entries)
            {
                var rowIndex = _restoreGrid.Rows.Add(ShouldAutoSelect(entry), entry.Product, entry.SourceVersion, entry.Category,
                    FormatBytes(entry.SizeBytes), "", "");
                _restoreGrid.Rows[rowIndex].Tag = entry;
            }
            AutoMapTargets();
            Log(_restoreLog, "Opened: " + _loadedManifest.SourceMachine + "\\" + _loadedManifest.SourceUser +
                ", " + _loadedManifest.CreatedUtc.ToLocalTime().ToString("g") +
                ". Settings sets: " + _loadedManifest.Entries.Count);
            Log(_restoreLog, "Cache and folders above " + _options.AutoSelectFolderLimitMb +
                             " MB require manual selection (0 means unlimited).");
        }
        catch (Exception ex) { ShowError(ex); Log(_restoreLog, ex.Message); }
        finally { SetBusy(false); }
    }

    private void AutoMapTargets()
    {
        foreach (DataGridViewRow row in _restoreGrid.Rows)
        {
            if (row.Tag is not BackupEntry entry) continue;
            var target = _discovery.FindTargetCandidates(entry).FirstOrDefault();
            var targetPath = target?.TargetPath ?? _discovery.ExpandPortablePath(entry.PortablePath);
            row.Cells["TargetPath"].Value = targetPath;
            row.Cells["TargetStatus"].Value = target?.Exists == true ? "exists" : "will be created";
        }
    }

    private void ChooseTargetForCurrentRow()
    {
        var row = _restoreGrid.CurrentRow;
        if (row?.Tag is not BackupEntry entry) return;
        if (entry.Kind == SourceKind.Registry)
        {
            MessageBox.Show(this, "Registry paths can be edited directly in the table.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the target folder for " + entry.Product + " — " + entry.Category,
            UseDescriptionForTitle = true, ShowNewFolderButton = true
        };
        var current = Convert.ToString(row.Cells["TargetPath"].Value);
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            row.Cells["TargetPath"].Value = dialog.SelectedPath;
            row.Cells["TargetStatus"].Value = Directory.Exists(dialog.SelectedPath) ? "exists" : "will be created";
        }
    }

    private void ShowPreview()
    {
        try
        {
            var preview = _restoreService.Preview(_packagePath.Text.Trim(), GetRestoreSelections());
            var text = "Files/entries to copy: " + preview.FilesToCopy +
                "\nAlready exist at target: " + preview.ExistingFiles +
                "\nTotal size: " + FormatBytes(preview.BytesToCopy) +
                "\nMissing from saved copy: " + preview.MissingPayloadFiles;
            if (preview.Warnings.Count > 0)
                text += "\n\nWarnings:\n• " + string.Join("\n• ", preview.Warnings.Distinct());
            MessageBox.Show(this, text, "Preview", MessageBoxButtons.OK,
                preview.MissingPayloadFiles > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RestoreAsync()
    {
        _restoreGrid.EndEdit();
        var selections = GetRestoreSelections();
        if (selections.Count == 0)
        {
            MessageBox.Show(this, "Select at least one settings set.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var running = RestoreService.FindRunningGraphicsApps();
        if (running.Count > 0)
        {
            MessageBox.Show(this, "Close these applications before restoring:\n\n" +
                string.Join("\n", running) +
                "\n\nThey may overwrite restored settings when they exit.",
                "Close applications first", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var preview = _restoreService.Preview(_packagePath.Text.Trim(), selections);
        var question = "Copy " + preview.FilesToCopy + " files/entries (" +
            FormatBytes(preview.BytesToCopy) + ")?\n\n" +
            "Existing files will be saved before replacement in Documents\\" +
            "GraphicsSettingsMigrator Rollbacks.\nNo extra files will be deleted from the target.";
        if (preview.Warnings.Count > 0)
            question += "\n\nWarning: cross-version migration is selected. Presets are generally portable, " +
                        "but binary preference files may be incompatible.";
        if (MessageBox.Show(this, question, "Confirm restore",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => Log(_restoreLog, message));
            var result = await _restoreService.RestoreAsync(
                _packagePath.Text.Trim(), selections, _overwrite.Checked, progress);
            MessageBox.Show(this, "Done.\n\nCopied: " + result.CopiedFiles +
                "\nSkipped: " + result.SkippedFiles + "\nRollback: " + result.RollbackPath,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); Log(_restoreLog, ex.ToString()); }
        finally { SetBusy(false); }
    }

    private async Task CheckForUpdatesAsync()
    {
        SetBusy(true);
        _updateButton.Text = "Checking...";
        try
        {
            var result = await UpdateService.CheckAsync();
            if (!result.IsAvailable)
            {
                MessageBox.Show(this,
                    "You already have the latest version (" + UpdateService.CurrentVersionText + ").",
                    "No updates available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var update = result.Update!;
            var answer = MessageBox.Show(this,
                "Graphics Settings Migrator " + UpdateService.CurrentVersionText +
                " can be updated to " + update.Version.ToString(3) + ".\n\n" +
                "Download: " + FormatBytes(update.SizeBytes) +
                "\nSource: GitHub Releases\nIntegrity: GitHub SHA-256 digest\n\n" +
                "The application will close, replace its portable files, and restart. Continue?",
                "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _updateButton.Text = "Downloading...";
            var progress = new Progress<string>(message => Log(_backupLog, message));
            await UpdateService.DownloadAndLaunchAsync(update, progress);
            Application.Exit();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Log(_backupLog, "Update failed: " + ex.Message);
        }
        finally
        {
            if (!IsDisposed)
            {
                _updateButton.Text = "Check for updates";
                SetBusy(false);
            }
        }
    }

    private void SavePathPreferences()
    {
        var preferences = _preferencesStore.Load();
        preferences.BackupDestination = _backupDestination.Text.Trim();
        preferences.RestorePackagePath = _packagePath.Text.Trim();
        preferences.OverwriteExistingFiles = _overwrite.Checked;
        _preferencesStore.Save(preferences);
    }

    private List<RestoreSelection> GetRestoreSelections()
    {
        if (_loadedManifest == null) throw new InvalidOperationException("Open a saved settings folder first.");
        return _restoreGrid.Rows.Cast<DataGridViewRow>().Where(IsSelected)
            .Where(x => x.Tag is BackupEntry)
            .Select(x => new RestoreSelection
            {
                Entry = (BackupEntry)x.Tag!,
                TargetPath = Convert.ToString(x.Cells["TargetPath"].Value)?.Trim() ?? ""
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetPath)).ToList();
    }

    private bool BrowseInto(TextBox textBox, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = true
        };
        if (Directory.Exists(textBox.Text)) dialog.InitialDirectory = textBox.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        textBox.Text = dialog.SelectedPath;
        return true;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _scanButton.Enabled = !busy;
        _removeButton.Enabled = !busy;
        _backupButton.Enabled = !busy;
        _autoSelectLimit.Enabled = !busy;
        _loadButton.Enabled = !busy;
        _toggleBackupButton.Enabled = !busy;
        _toggleRestoreButton.Enabled = !busy;
        _updateButton.Enabled = !busy;
        _previewButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
    }

    private void ToggleAll(DataGridView grid)
    {
        grid.EndEdit();
        var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
        if (rows.Count == 0) return;
        var automaticRows = rows.Where(IsAutomaticallySelectableRow).ToList();
        var selectAll = automaticRows.Any(row => !IsSelected(row));
        if (selectAll)
        {
            foreach (var row in automaticRows) row.Cells["Selected"].Value = true;
        }
        else
        {
            foreach (var row in rows) row.Cells["Selected"].Value = false;
        }
        grid.InvalidateColumn(grid.Columns["Selected"].Index);
    }

    private static void ConfigureMultiRowSelection(DataGridView grid)
    {
        List<DataGridViewRow>? clickRows = null;
        var applyingGroupValue = false;
        var selectedColumn = grid.Columns["Selected"].Index;

        grid.CellMouseDown += (_, e) =>
        {
            clickRows = null;
            if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex != selectedColumn) return;
            var clickedRow = grid.Rows[e.RowIndex];
            var selectedRows = grid.SelectedRows.Cast<DataGridViewRow>().ToList();
            if (selectedRows.Count > 1 && selectedRows.Contains(clickedRow)) clickRows = selectedRows;
        };

        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell?.ColumnIndex == selectedColumn)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        grid.CellValueChanged += (_, e) =>
        {
            if (applyingGroupValue || e.RowIndex < 0 || e.ColumnIndex != selectedColumn) return;
            var rows = clickRows;
            clickRows = null;
            if (rows is not { Count: > 1 } || !rows.Contains(grid.Rows[e.RowIndex])) return;

            var value = IsSelected(grid.Rows[e.RowIndex]);
            applyingGroupValue = true;
            try
            {
                foreach (var row in rows.Where(row => !row.IsNewRow))
                    row.Cells["Selected"].Value = value;
            }
            finally
            {
                applyingGroupValue = false;
            }
            grid.InvalidateColumn(selectedColumn);
        };

        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || grid.SelectedRows.Count == 0) return;
            ToggleRows(grid, grid.SelectedRows.Cast<DataGridViewRow>());
            e.Handled = true;
            e.SuppressKeyPress = true;
        };
    }

    private static void ToggleRows(DataGridView grid, IEnumerable<DataGridViewRow> sourceRows)
    {
        grid.EndEdit();
        var rows = sourceRows.Where(row => !row.IsNewRow).Distinct().ToList();
        if (rows.Count == 0) return;
        var selected = rows.Any(row => !IsSelected(row));
        foreach (var row in rows) row.Cells["Selected"].Value = selected;
        grid.InvalidateColumn(grid.Columns["Selected"].Index);
    }

    private static bool IsCacheRow(DataGridViewRow row) => row.Tag switch
    {
        SettingsLocation location => IsCacheSet(location.Category, location.Notes),
        BackupEntry entry => IsCacheSet(entry.Category, entry.Notes),
        _ => false
    };

    private void AutoSelectLimitChanged()
    {
        _options.AutoSelectFolderLimitMb = (int)_autoSelectLimit.Value;
        try { _options.Save(); }
        catch (Exception ex) { Log(_backupLog, "Could not save the auto-selection limit: " + ex.Message); }

        foreach (DataGridViewRow row in _backupGrid.Rows)
        {
            if (row.Tag is not SettingsLocation location || IsCacheSet(location.Category, location.Notes))
                continue;
            row.Cells["Selected"].Value = ShouldAutoSelect(location);
            row.Cells["Notes"].Value = DisplayNotes(location);
            UpdateBackupRowAppearance(row, location);
        }
        foreach (DataGridViewRow row in _restoreGrid.Rows)
        {
            if (row.Tag is not BackupEntry entry || IsCacheSet(entry.Category, entry.Notes)) continue;
            row.Cells["Selected"].Value = ShouldAutoSelect(entry);
        }
        _backupGrid.Invalidate();
        _restoreGrid.Invalidate();
    }

    private bool ShouldAutoSelect(SettingsLocation location) =>
        location.Recommended && !IsCacheSet(location.Category, location.Notes) &&
        !IsOverAutoSelectLimit(location.Kind, location.SizeBytes);

    private bool ShouldAutoSelect(BackupEntry entry) =>
        !IsCacheSet(entry.Category, entry.Notes) &&
        !IsOverAutoSelectLimit(entry.Kind, entry.SizeBytes);

    private bool IsAutomaticallySelectableRow(DataGridViewRow row)
    {
        if (IsCacheRow(row)) return false;
        return row.Tag switch
        {
            SettingsLocation location => !IsOverAutoSelectLimit(location.Kind, location.SizeBytes),
            BackupEntry entry => !IsOverAutoSelectLimit(entry.Kind, entry.SizeBytes),
            _ => true
        };
    }

    private bool IsOverAutoSelectLimit(SourceKind kind, long sizeBytes) =>
        kind == SourceKind.Directory && sizeBytes > _options.AutoSelectFolderLimitBytes;

    private string DisplayNotes(SettingsLocation location)
    {
        if (!IsOverAutoSelectLimit(location.Kind, location.SizeBytes)) return location.Notes;
        var suffix = "Auto-selection skipped: folder exceeds " +
                     _options.AutoSelectFolderLimitMb.ToString("N0", CultureInfo.CurrentCulture) + " MB.";
        return string.IsNullOrWhiteSpace(location.Notes) ? suffix : location.Notes + " " + suffix;
    }

    private void UpdateBackupRowAppearance(DataGridViewRow row, SettingsLocation location)
    {
        row.DefaultCellStyle.ForeColor = ShouldAutoSelect(location)
            ? Color.Empty
            : Color.FromArgb(165, 168, 175);
    }


    private static bool IsCacheSet(string category, string notes) =>
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

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
        AutoGenerateColumns = false, BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None
    };

    private static TextBox CreateLog() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window, Font = new Font("Consolas", 8.5F)
    };

    private static void Log(TextBox box, string message) =>
        box.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);

    private void ShowError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
