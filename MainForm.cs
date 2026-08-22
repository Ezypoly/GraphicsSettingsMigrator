using System.Globalization;

namespace GraphicsSettingsMigrator;

public sealed class MainForm : Form
{
    private readonly DiscoveryService _discovery = new();
    private readonly BackupService _backupService = new();
    private readonly RestoreService _restoreService = new();
    private readonly DataGridView _backupGrid = CreateGrid();
    private readonly DataGridView _restoreGrid = CreateGrid();
    private readonly TextBox _backupDestination = new();
    private readonly TextBox _packagePath = new();
    private readonly TextBox _backupLog = CreateLog();
    private readonly TextBox _restoreLog = CreateLog();
    private readonly Button _scanButton = new() { Text = "Scan", AutoSize = true };
    private readonly Button _backupButton = new() { Text = "Create backup", AutoSize = true };
    private readonly Button _loadButton = new() { Text = "Load backup", AutoSize = true };
    private readonly Button _previewButton = new() { Text = "Preview", AutoSize = true };
    private readonly Button _restoreButton = new() { Text = "Restore", AutoSize = true };
    private readonly Button _updateButton = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button _toggleBackupButton = new() { Text = "Select / clear all", AutoSize = true };
    private readonly Button _toggleRestoreButton = new() { Text = "Select / clear all", AutoSize = true };
    private readonly CheckBox _overwrite = new()
    {
        Text = "Overwrite existing files (with rollback backup)",
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
        _backupDestination.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GraphicsSettingsBackups");
        ConfigureBackupGrid();
        ConfigureRestoreGrid();
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
        _toggleRestoreButton.Click += (_, _) => ToggleAll(_restoreGrid);
        Shown += async (_, _) => await ScanAsync();
    }

    private TabPage BuildBackupTab()
    {
        var page = new TabPage("Backup");
        var layout = NewLayout();
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Find existing settings, select the required sets, and create a portable backup folder. " +
                   "ZBrush QuickSave and Temp data are not included."
        };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 6)
        };
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseInto(_backupDestination, "Choose where to save the backup");
        _backupDestination.Width = 470;
        actions.Controls.Add(_toggleBackupButton);
        actions.Controls.Add(_scanButton);
        actions.Controls.Add(new Label { Text = "  Save to:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        actions.Controls.Add(_backupDestination);
        actions.Controls.Add(browse);
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
        var page = new TabPage("Restore / migrate");
        var layout = NewLayout();
        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Select a folder containing manifest.json. Targets are matched to installed versions automatically; " +
                   "you can edit any target path manually. Restore merges files and never deletes extra target files."
        };
        var top = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 6, 0, 6)
        };
        _packagePath.Width = 420;
        var browse = new Button { Text = "Backup…", AutoSize = true };
        browse.Click += (_, _) => BrowseInto(_packagePath, "Select a backup folder");
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
        _backupGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Back up", Width = 75 });
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
                var rowIndex = _backupGrid.Rows.Add(location.Recommended, location.Product, location.Version,
                    location.Category, FormatBytes(location.SizeBytes),
                    location.Kind == SourceKind.Registry ? "registry" : location.FileCount,
                    location.SourcePath, location.Notes);
                var row = _backupGrid.Rows[rowIndex];
                row.Tag = location;
                if (!location.Recommended) row.DefaultCellStyle.ForeColor = Color.FromArgb(165, 168, 175);
            }
            Log(_backupLog, "Settings sets found: " + locations.Count +
                            ". Optional or cache-containing sets are shown in gray.");
            Log(_backupLog, "Supported application catalog: " + (ExtendedDiscovery.SupportedProducts.Length + 12) + " products.");
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
                "\n\nClose them first for the most up-to-date backup. Continue anyway?",
                "Applications are running", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => Log(_backupLog, message));
            var package = await _backupService.CreateBackupAsync(
                selected, _backupDestination.Text.Trim(), progress);
            MessageBox.Show(this, "Backup created:\n\n" + package,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); Log(_backupLog, ex.ToString()); }
        finally { SetBusy(false); }
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
                var rowIndex = _restoreGrid.Rows.Add(true, entry.Product, entry.SourceVersion, entry.Category,
                    FormatBytes(entry.SizeBytes), "", "");
                _restoreGrid.Rows[rowIndex].Tag = entry;
            }
            AutoMapTargets();
            Log(_restoreLog, "Backup: " + _loadedManifest.SourceMachine + "\\" + _loadedManifest.SourceUser +
                ", " + _loadedManifest.CreatedUtc.ToLocalTime().ToString("g") +
                ". Settings sets: " + _loadedManifest.Entries.Count);
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
                "\nMissing from backup: " + preview.MissingPayloadFiles;
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

    private List<RestoreSelection> GetRestoreSelections()
    {
        if (_loadedManifest == null) throw new InvalidOperationException("Load a backup folder first.");
        return _restoreGrid.Rows.Cast<DataGridViewRow>().Where(IsSelected)
            .Where(x => x.Tag is BackupEntry)
            .Select(x => new RestoreSelection
            {
                Entry = (BackupEntry)x.Tag!,
                TargetPath = Convert.ToString(x.Cells["TargetPath"].Value)?.Trim() ?? ""
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.TargetPath)).ToList();
    }

    private void BrowseInto(TextBox textBox, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = true
        };
        if (Directory.Exists(textBox.Text)) dialog.InitialDirectory = textBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) textBox.Text = dialog.SelectedPath;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _scanButton.Enabled = !busy;
        _backupButton.Enabled = !busy;
        _loadButton.Enabled = !busy;
        _toggleBackupButton.Enabled = !busy;
        _toggleRestoreButton.Enabled = !busy;
        _updateButton.Enabled = !busy;
        _previewButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
    }

    private static void ToggleAll(DataGridView grid)
    {
        grid.EndEdit();
        var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
        if (rows.Count == 0) return;
        var selectAll = rows.Any(row => !IsSelected(row));
        foreach (var row in rows)
            row.Cells["Selected"].Value = selectAll;
        grid.InvalidateColumn(grid.Columns["Selected"].Index);
    }

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
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
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
