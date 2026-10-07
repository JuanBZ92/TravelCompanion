namespace TravelCompanion.Mobile.Services;

public sealed partial class JournalStore
{
    // New photo payloads are local and encrypted; only the draft references them until confirmation.
    public async Task<JournalMemory> AddDraftPhotosAsync(JournalScope scope, JournalMemory memory,
        string text, IEnumerable<FileResult> files)
    {
        CheckEntry(scope, memory);
        var normalized = new List<JournalImageData>();
        foreach (var file in files.Take(10 - memory.Images.Length))
        {
            Check(scope);
            await using var input = await file.OpenReadAsync();
            normalized.Add(await JournalMedia.NormalizeAsync(input));
        }
        if (normalized.Count == 0) return memory;
        await gate.WaitAsync();
        var created = new List<Guid>();
        try
        {
            Check(scope);
            foreach (var image in normalized)
            {
                var id = Guid.NewGuid();
                created.Add(id);
                await SavePhotoPayloadAsync(scope, id, image);
                memory = memory with { Photos = [..memory.Images, new(id)], CoverId = memory.CoverId ?? id };
                Check(scope);
            }
            var drafts = await ReadDraftsAsync(scope, default);
            Check(scope);
            drafts.RemoveAll(x => x.Memory.Key == memory.Key);
            drafts.Add(new(memory, text, DateTimeOffset.UtcNow));
            await cache.SaveAsync(Prefix(scope) + "drafts", drafts);
        }
        catch { foreach (var id in created) await DeletePhotoPayloadAsync(scope, id); throw; }
        finally { gate.Release(); }
        return memory;
    }
}
