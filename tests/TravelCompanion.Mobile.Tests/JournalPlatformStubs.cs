public sealed class FileResult
{
    public Func<Task<Stream>>? Read { get; init; }
    public Task<Stream> OpenReadAsync() => Read?.Invoke() ?? Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
}

namespace TravelCompanion.Mobile.Services
{
    public sealed record JournalImageData(byte[] Image, byte[] Thumbnail);
    public static class JournalMedia
    {
        public static Task<JournalImageData> NormalizeAsync(Stream input) => Task.FromResult(new JournalImageData([1], [2]));
    }
}
