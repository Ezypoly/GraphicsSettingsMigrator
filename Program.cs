namespace GraphicsSettingsMigrator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (UpdateService.TryApplyUpdate(args)) return;
        UpdateService.ScheduleCleanup(args);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
