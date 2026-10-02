namespace TravelCompanion.Mobile.Services;

public static partial class JournalPdfExporter
{
    public static partial Task<string> CreateAsync(JournalStore store, JournalScope scope, string title,
        IReadOnlyList<JournalMemory> entries, Guid? cover, IProgress<double> progress, CancellationToken ct)
    {

        return Task.Run(async () =>
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"Journal-{Guid.NewGuid():N}.pdf");
            using var pdf = new Android.Graphics.Pdf.PdfDocument();
            using var paint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias);
            Android.Graphics.Pdf.PdfDocument.Page? page = null;
            var pageNumber = 0;
            var y = 0f;
            void NewPage()
            {
                if (page is not null) { pdf.FinishPage(page); page = null; }
                ct.ThrowIfCancellationRequested();
                if (!store.IsCurrent(scope)) throw new OperationCanceledException();
                using var info = new Android.Graphics.Pdf.PdfDocument.PageInfo.Builder(595, 842, ++pageNumber).Create();
                page = pdf.StartPage(info) ?? throw new InvalidOperationException("No se pudo crear una página del Journal.");
                page.Canvas!.DrawColor(Android.Graphics.Color.Rgb(248, 243, 237));
                paint.Color = Android.Graphics.Color.Rgb(113, 103, 93); paint.TextSize = 10;
                page.Canvas!.DrawText($"YUKU · JOURNAL     {pageNumber}", 42, 812, paint);
                y = 58;
            }
            void Text(string text, float size, bool serif = false)
            {
                paint.TextSize = size; paint.Color = Android.Graphics.Color.Rgb(48, 41, 32);
                paint.SetTypeface(serif ? Android.Graphics.Typeface.Serif : Android.Graphics.Typeface.SansSerif);
                foreach (var paragraph in text.Replace("\r", "").Split('\n'))
                {
                    var remaining = paragraph;
                    if (remaining.Length == 0) { y += size; continue; }
                    while (remaining.Length > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        var count = Math.Max(1, paint.BreakText(remaining, true, 511, null));
                        if (count < remaining.Length && remaining.LastIndexOf(' ', count - 1, count) is var space && space > 0) count = space;
                        if (y + size * 1.5f > 780) { NewPage(); paint.TextSize = size; paint.Color = Android.Graphics.Color.Rgb(48, 41, 32); }
                        page!.Canvas!.DrawText(remaining[..count], 42, y, paint);
                        remaining = remaining[count..].TrimStart(); y += size * 1.5f;
                    }
                }
                y += 10;
            }
            async Task Photo(Guid id, float maxHeight)
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await store.PhotoAsync(scope, id);
                if (bytes is null) return;
                using var bitmap = Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (bitmap is null) return;
                var scale = Math.Min(511f / bitmap.Width, maxHeight / bitmap.Height);
                var height = bitmap.Height * scale; var width = bitmap.Width * scale;
                if (y + height > 780) NewPage();
                using var destination = new Android.Graphics.RectF(42 + (511 - width) / 2, y, 42 + (511 + width) / 2, y + height);
                page!.Canvas!.DrawBitmap(bitmap, null, destination, null);
                y += height + 22;
            }
            try
            {
                NewPage(); y = 110; Text(title, 32, true);
                Text($"{entries.Min(x => x.Date):d MMMM yyyy} — {entries.Max(x => x.Date):d MMMM yyyy}", 13);
                Text(string.Join(" · ", entries.Select(x => x.City).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()), 13);
                if (cover.HasValue) await Photo(cover.Value, 420);
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i]; NewPage();
                    Text(entry.Date.ToString("d MMMM yyyy") + (string.IsNullOrWhiteSpace(entry.City) ? "" : $" · {entry.City}"), 12);
                    Text(JournalText.Title(entry), 26, true);
                    Text(entry.Text, 13);
                    foreach (var photo in entry.Images.OrderByDescending(x => x.Id == entry.CoverId)) await Photo(photo.Id, 420);
                    progress.Report((double)(i + 1) / entries.Count);
                }
                if (page is not null) { pdf.FinishPage(page); page = null; }
                ct.ThrowIfCancellationRequested();
                if (!store.IsCurrent(scope)) throw new OperationCanceledException();
                using var output = File.Create(path); pdf.WriteTo(output);
                return path;
            }
            catch
            {
                if (page is not null) pdf.FinishPage(page);
                if (File.Exists(path)) File.Delete(path);
                throw;
            }
        }, ct);
    }
}
