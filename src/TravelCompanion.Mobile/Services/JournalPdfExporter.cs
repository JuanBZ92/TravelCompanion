namespace TravelCompanion.Mobile.Services;

public static partial class JournalPdfExporter
{
    public static string DirectoryPath => Path.Combine(FileSystem.CacheDirectory, "journal-export");
    public static Task ClearAsync()
    {
        if (Directory.Exists(DirectoryPath))
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.pdf"))
                try { File.Delete(file); } catch (IOException) { }
        return Task.CompletedTask;
    }

    public static partial Task<string> CreateAsync(JournalStore store, JournalScope scope, string title,
        IReadOnlyList<JournalMemory> entries, Guid? cover, IProgress<double> progress, CancellationToken ct);

#if !ANDROID
    public static partial Task<string> CreateAsync(JournalStore store, JournalScope scope, string title,
        IReadOnlyList<JournalMemory> entries, Guid? cover, IProgress<double> progress, CancellationToken ct) =>
        throw new PlatformNotSupportedException("La exportación de Journal está disponible en Android.");
#endif
}
