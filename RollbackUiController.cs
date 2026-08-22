using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace GraphicsSettingsMigrator;

internal static class RollbackUiBootstrap
{
    private static readonly ConditionalWeakTable<MainForm, RollbackUiController> Controllers = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += AttachToOpenForms;
    }

    private static void AttachToOpenForms(object? sender, EventArgs eventArgs)
    {
        foreach (var form in Application.OpenForms.OfType<MainForm>())
            if (!Controllers.TryGetValue(form, out _))
                Controllers.Add(form, new RollbackUiController(form));
    }
}

internal sealed class RollbackUiController
{
    private readonly MainForm _form;
    private readonly RollbackService _service = new();
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5F)
    };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _open = new() { Text = "Open folder", AutoSize = true };
    private readonly Button _revert = new() { Text = "Revert selected restore", AutoSize = true };

    public RollbackUiController(MainForm form)
    {
        _form = form;
        var tabs = Descendants<TabControl>(form).FirstOrDefault();
        if (tabs == null) return;
        tabs.TabPages.Add(BuildTab());
        _refresh.Click += (_, _) => RefreshList();
        _open.Click += (_, _) => OpenSelected();
        _revert.Click += async (_, _) => await RevertSelectedAsync();
        RefreshList();
    }

    private TabPage BuildTab()
    {
        var page = new TabPage("Rollback");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1120, 0),
            Text = "Restore the exact state saved automatically before a previous restore. " +
                   "Legacy rollback folders without a manifest remain available for manual recovery."
        }, 0, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(0, 6, 0, 6)
        };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(_open);
        actions.Controls.Add(_revert);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_list, 0, 2);
        layout.Controls.Add(_log, 0, 3);
        page.Controls.Add(layout);
        DarkTheme.Apply(page);
        return page;
    }

    private void RefreshList()
    {
        var selectedPath = (_list.SelectedItem as RollbackPackage)?.FolderPath;
        _list.Items.Clear();
        foreach (var package in _service.Discover()) _list.Items.Add(package);
        if (selectedPath != null)
            foreach (var item in _list.Items.OfType<RollbackPackage>())
                if (item.FolderPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase))
                    _list.SelectedItem = item;
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
        Log("Rollback folders found: " + _list.Items.Count);
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is not RollbackPackage package) return;
        Process.Start(new ProcessStartInfo("explorer.exe", package.FolderPath) { UseShellExecute = true });
    }

    private async Task RevertSelectedAsync()
    {
        if (_list.SelectedItem is not RollbackPackage package) return;
        if (!package.CanRevert)
        {
            MessageBox.Show(_form, "This legacy rollback has no manifest and can only be restored manually.",
                "Rollback", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var running = RestoreService.FindRunningGraphicsApps();
        if (running.Count > 0)
        {
            MessageBox.Show(_form, "Close these applications before reverting:\n\n" +
                string.Join("\n", running), "Close applications first",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var repeat = package.WasReverted ? "\n\nThis rollback was already applied once." : "";
        var question = "Restore overwritten files and registry settings from:\n\n" + package.FolderPath +
                       "\n\nFiles created by the original restore will be deleted. " +
                       "Unrelated files will not be touched, and files changed since the restore will be skipped." +
                       repeat;
        if (MessageBox.Show(_form, question, "Confirm rollback",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        SetBusy(true);
        try
        {
            var progress = new Progress<string>(Log);
            var result = await _service.RevertAsync(package.FolderPath, progress);
            MessageBox.Show(_form, "Rollback completed.\n\nRestored files: " + result.RestoredFiles +
                "\nRemoved files created by restore: " + result.RemovedFiles +
                "\nRestored registry sets: " + result.RestoredRegistryKeys +
                "\nSkipped files changed since restore: " + result.SkippedChangedFiles,
                "Rollback", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshList();
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            MessageBox.Show(_form, ex.Message, "Rollback error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _form.UseWaitCursor = busy;
        _refresh.Enabled = !busy;
        _open.Enabled = !busy;
        _revert.Enabled = !busy;
    }

    private void Log(string message) =>
        _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
