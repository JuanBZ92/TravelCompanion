namespace TravelCompanion.Mobile.Services;

public sealed record JournalImageData(byte[] Image, byte[] Thumbnail);

public static partial class JournalMedia
{
    // The picker applies EXIF orientation, strips metadata and bounds dimensions before this step.
    public static MediaPickerOptions PickerOptions(int remaining) => new()
    {
        Title = JournalText.Get("JournalPhotoPicker"), SelectionLimit = remaining, MaximumWidth = 2048,
        MaximumHeight = 2048, RotateImage = true, PreserveMetaData = false, CompressionQuality = 85
    };

    public static async Task<JournalImageData> NormalizeAsync(Stream input)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > 24 * 1024 * 1024) throw new InvalidOperationException(JournalText.Get("JournalPhotoTooLarge"));
            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }
        var bytes = buffer.ToArray();
        return await NormalizeBytesAsync(bytes);
    }

    private static partial Task<JournalImageData> NormalizeBytesAsync(byte[] bytes);
#if !ANDROID
    private static partial Task<JournalImageData> NormalizeBytesAsync(byte[] bytes) => Task.FromResult(new JournalImageData(bytes, bytes));
#endif
}
