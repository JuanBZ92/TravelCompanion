namespace TravelCompanion.Mobile.Services;

public static class LocalDocumentPolicy
{
    public const int MaximumBytes = 20 * 1024 * 1024;

    public static string Extension(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => ".pdf", ".jpg" or ".jpeg" => ".jpg", ".png" => ".png",
        _ => throw new InvalidDataException("Only PDF, JPEG and PNG files are supported.")
    };

    public static async Task<byte[]> ReadAsync(Stream source, string fileName, CancellationToken ct = default)
    {
        var extension = Extension(fileName);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (destination.Length + count > MaximumBytes) throw new InvalidDataException("File exceeds 20 MB.");
            await destination.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = destination.ToArray();
        var valid = extension switch
        {
            ".pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            ".png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            _ => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255
        };
        if (!valid) throw new InvalidDataException("File content does not match its type.");
        return bytes;
    }
}
