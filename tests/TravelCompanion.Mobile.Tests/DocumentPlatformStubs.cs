// Native viewers are covered by device validation, not these storage tests.
public static class FileSystem
{
    public static string CacheDirectory => Path.Combine(Path.GetTempPath(), "travelcompanion-document-tests");
}
public sealed record ReadOnlyFile(string Path);
public sealed record OpenFileRequest(string Title, ReadOnlyFile File);
public static class Launcher
{
    public static Task<bool> OpenAsync(OpenFileRequest request) => Task.FromResult(true);
}
