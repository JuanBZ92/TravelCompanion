// Native viewers are covered by device validation, not these storage tests.
public static class FileSystem
{
    public static string CacheDirectory => Path.Combine(Path.GetTempPath(), "travelcompanion-document-tests");
}
public sealed record ReadOnlyFile(string Path);
public sealed record OpenFileRequest(string Title, ReadOnlyFile File);
public static class Launcher
{
    public static LauncherState Default { get; } = new();
    public sealed class LauncherState { public Task<bool> OpenAsync(Uri uri) => Launcher.OpenAsync(uri); }
    public static int OpenFileCalls;
    public static Task<bool> OpenAsync(OpenFileRequest request)
    { OpenFileCalls++; return Task.FromResult(true); }
    public static Task<bool> OpenAsync(Uri uri) => Task.FromResult(true);
}
