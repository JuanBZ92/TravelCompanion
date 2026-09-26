using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class OfflinePreparationTests
{
    [Fact]
    public async Task Accepts_non_seekable_document_streams_and_checks_content()
    {
        await using var source = new NonSeekableStream("%PDF-1.7 test"u8.ToArray());
        var result = await LocalDocumentPolicy.ReadAsync(source, "ticket.pdf");
        Assert.Equal("%PDF-1.7 test"u8.ToArray(), result);
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalDocumentPolicy.ReadAsync(new MemoryStream("<html>"u8.ToArray()), "ticket.pdf"));
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalDocumentPolicy.ReadAsync(new MemoryStream([]), "ticket.exe"));
    }

    [Fact]
    public async Task Rejects_files_over_limit_without_relying_on_stream_length()
    {
        await using var source = new NonSeekableStream(new byte[LocalDocumentPolicy.MaximumBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalDocumentPolicy.ReadAsync(source, "large.pdf"));
    }

    [Fact]
    public void Pending_or_interrupted_download_is_never_ready()
    {
        var ready = new OfflineTripManifest(Guid.NewGuid(), 3, DateTimeOffset.UtcNow,
            [new("itinerary", "Trip", "available"), new("link", "Website", "external")]);
        Assert.True(ready.IsComplete);
        Assert.False((ready with { Interrupted = true }).IsComplete);
        Assert.False((ready with { Resources = [.. ready.Resources, new("file", "Ticket", "pending")] }).IsComplete);
        Assert.False((ready with { DownloadedAt = null }).IsComplete);
        Assert.False((ready with { Resources = [] }).IsComplete);
        Assert.True(ready.HasUpdate(4));
        Assert.False(ready.HasUpdate(3));
        var versioned = ready with { CatalogVersion = 7, DocumentsVersion = 11 };
        Assert.False(versioned.HasUpdate(3, 7, 11));
        Assert.True(versioned.HasUpdate(3, 8, 11));
        Assert.True(versioned.HasUpdate(3, 7, 12));
        Assert.True(ready.HasUpdate(3, 7, 11));
        Assert.False(versioned.HasUpdate(3)); // Offline: no newer version is known.
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
