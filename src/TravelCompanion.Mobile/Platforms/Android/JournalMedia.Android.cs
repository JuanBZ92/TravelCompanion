namespace TravelCompanion.Mobile.Services;

public static partial class JournalMedia
{
    private static async partial Task<JournalImageData> NormalizeBytesAsync(byte[] bytes)
    {

        return await Task.Run(() =>
        {
            using var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds)?.Dispose();
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidOperationException("No se pudo leer esta foto.");
            var sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 4096) sample *= 2;
            using var options = new Android.Graphics.BitmapFactory.Options { InSampleSize = sample };
            using var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options)
                ?? throw new InvalidOperationException("No se pudo leer esta foto.");
            return new JournalImageData(Encode(bitmap, 2048), Encode(bitmap, 512));
        });
    }

    private static byte[] Encode(Android.Graphics.Bitmap bitmap, int limit)
    {
        var scale = Math.Min(1d, (double)limit / Math.Max(bitmap.Width, bitmap.Height));
        if (scale == 1)
        {
            using var originalOutput = new MemoryStream();
            bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 85, originalOutput);
            return originalOutput.ToArray();
        }
        using var result = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap,
            Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)), true);
        using var output = new MemoryStream();
        result.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 85, output);
        return output.ToArray();
    }
}
