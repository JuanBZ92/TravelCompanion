using System.Collections.Concurrent;
using TravelCompanion.Shared.Dtos;

// Host-only adapters. The actual Journal/Expense stores and AES-GCM cache are compiled unchanged,
// except for a transparent file-read counter in the staged cache source. No phone or API is used.
public static class FileSystem
{
    public static string AppDataDirectory { get; set; } = "";
}

public sealed class SecureStorage
{
    public static SecureStorage Default { get; } = new();
    private readonly ConcurrentDictionary<string, string> values = new();
    public Task<string?> GetAsync(string key) => Task.FromResult(values.GetValueOrDefault(key));
    public Task SetAsync(string key, string value) { values[key] = value; return Task.CompletedTask; }
}

public sealed class FileResult
{
    public Task<Stream> OpenReadAsync() => Task.FromResult<Stream>(new MemoryStream([1]));
}

namespace TravelCompanion.Mobile.Services
{
    public sealed record JournalImageData(byte[] Image, byte[] Thumbnail);
    public static class JournalMedia
    {
        private static readonly JournalImageData Data = Create();
        private static JournalImageData Create()
        {
            var random = new Random(73382);
            var image = new byte[2 * 1024 * 1024]; var thumbnail = new byte[8 * 1024];
            random.NextBytes(image); random.NextBytes(thumbnail);
            return new(image, thumbnail);
        }
        public static Task<JournalImageData> NormalizeAsync(Stream input) => Task.FromResult(Data);
    }

    public sealed class LocalizationResourceManager
    {
        public static LocalizationResourceManager Instance { get; } = new();
        public string this[string key] => key;
    }

    public sealed class AuthSessionService(Guid user, Guid trip)
    {
        public bool HasSession => true;
        public bool HasKnownValidAccess => true;
        public bool IsFreeMapPreview => false;
        public Guid? CurrentUserId => user;
        public Guid? CurrentTripId => trip;
        public long ContextVersion => 1;
        public Task<string?> GetTokenAsync() => Task.FromResult<string?>("synthetic-local-token");
    }

    public sealed class TravelCompanionApiClient
    {
        public TaskCompletionSource Entered { get; private set; } = NewSignal();
        public TaskCompletionSource Release { get; private set; } = NewSignal();
        public int Requests;
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset() { Requests = 0; Entered = NewSignal(); Release = NewSignal(); }
        private async Task HoldAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Requests); Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            throw new HttpRequestException("Synthetic unavailable backend after controlled delay");
        }
        public async Task<List<JournalNoteDto>> GetJournalAsync(string token, Guid trip, CancellationToken ct)
        { await HoldAsync(ct); return []; }
        public Task<JournalSaveResult> SaveJournalAsync(string token, Guid trip, Guid id, SaveJournalNoteRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public Task<List<JournalFreeEntryDto>> GetJournalFreeAsync(string token, Guid trip, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public Task<JournalFreeSaveResult> SaveJournalFreeAsync(string token, Guid trip, Guid id, SaveJournalFreeEntryRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public Task<JournalFreeSaveResult> DeleteJournalFreeAsync(string token, Guid trip, Guid id, DeleteJournalFreeEntryRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public async Task<ExpensesDto> GetExpensesAsync(string token, Guid trip, CancellationToken ct)
        { await HoldAsync(ct); return null!; }
        public Task<SaveExpenseResult> SaveExpenseAsync(string token, Guid trip, Guid id, SaveExpenseRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public Task<ExpenseSettingsDto?> SaveExpenseSettingsAsync(string token, Guid trip, SaveExpenseSettingsRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
        public Task<ExpenseRateDto?> GetExpenseRateAsync(string token, Guid trip, string currency, string target, DateOnly date, CancellationToken ct) => throw new InvalidOperationException("Unexpected request");
    }
}

public static class BenchmarkIo
{
    public static long Reads;
    public static long BytesRead;
    public static void Reset() { Reads = 0; BytesRead = 0; }
    public static Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        Interlocked.Increment(ref Reads);
        Interlocked.Add(ref BytesRead, new FileInfo(path).Length);
        return File.ReadAllTextAsync(path, ct);
    }
}
