namespace TravelCompanion.Mobile.Services;

public static class ClientDiagnostics
{
    private static DiagnosticJournal? journal;
    private static int initialized;
    private static int sharing;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0) return;
        try
        {
            journal = new DiagnosticJournal(Path.Combine(FileSystem.AppDataDirectory, "diagnostics"));
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Record("unhandled_exception", new() { IsTerminating = args.IsTerminating }, args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
                Record("unobserved_task", exception: args.Exception);
            Record("process_start", EnvironmentDetails());
        }
        catch { /* Startup must also work if diagnostics storage is unavailable. */ }
    }

    // Callers supply code-defined fields only, never user input or formatted log messages.
    public static void Record(string name, DiagnosticDetails? details = null, Exception? exception = null) =>
        journal?.Write(name, details, exception);

    private static DiagnosticDetails EnvironmentDetails() => new()
    {
        Version = AppInfo.Current.VersionString, Build = AppInfo.Current.BuildString,
        Platform = DeviceInfo.Current.Platform.ToString(), Os = DeviceInfo.Current.VersionString,
        Model = DeviceInfo.Current.Model
    };

    public static async Task ShareAsync(Page page)
    {
        if (Interlocked.Exchange(ref sharing, 1) != 0) return;
        try
        {
            var text = LocalizationResourceManager.Instance;
            if (!await page.DisplayAlertAsync(text["DiagnosticsTitle"], text["DiagnosticsConsent"],
                    text["DiagnosticsShare"], text["DiagnosticsCancel"])) return;
            if (journal is null) throw new IOException("Diagnostics unavailable");
            Record("diagnostics_export", EnvironmentDetails());
            var directory = Path.Combine(FileSystem.CacheDirectory, "diagnostics-export");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "yuku-diagnostics.txt");
            await File.WriteAllTextAsync(path, journal.Snapshot());
            await Share.Default.RequestAsync(new ShareFileRequest(text["DiagnosticsTitle"], new ShareFile(path)));
        }
        catch (Exception exception)
        {
            Record("diagnostics_export_failed", exception: exception);
            await page.DisplayAlertAsync("YUKU", LocalizationResourceManager.Instance["DiagnosticsFailed"], "OK");
        }
        finally { Volatile.Write(ref sharing, 0); }
    }
}
