namespace Quick;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_version =>
            {
                QuickSettingsStore.TrySetLaunchAtStartup(Application.ExecutablePath, false, out _);
            })
            .Run();

        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            return;
        }

        using var singleInstance = new Mutex(true, "Quick.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using var form = new LauncherForm();
        _ = form.Handle;
        Application.Run(new LauncherApplicationContext(form));
    }
}

internal sealed class LauncherApplicationContext : ApplicationContext
{
    public LauncherApplicationContext(Form mainForm)
    {
        MainForm = mainForm;
    }
}
