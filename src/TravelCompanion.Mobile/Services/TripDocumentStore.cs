using System.Net.Http.Headers;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed record LocalTripDocument(Guid Id, string Title, string Extension, long Size, DateTimeOffset SavedAt, string? SourceUrl = null);
public sealed record LocalDocumentPayload(byte[] Bytes);

// Personal files use the existing encrypted, atomic cache writer, but a separate
// namespace: ordinary session/cache invalidation must not delete user attachments.
public sealed class TripDocumentStore(OfflineCacheService cache, AuthSessionService sessions, TravelCompanionApiClient api)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<Guid> _deletedAccounts = [];
    private readonly HashSet<(Guid User, Guid Trip)> _deletedTrips = [];
    private static readonly HttpClient DownloadClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(60) };

    public bool CanAttach => sessions.HasKnownValidAccess && sessions.IsBuilder && !sessions.IsFreeMapPreview
        && sessions.CanEditItinerary && sessions.CurrentTripId.HasValue;

    private (Guid User, Guid Trip) Scope() => sessions.HasSession && sessions.CurrentUserId is { } user && sessions.CurrentTripId is { } trip
        ? (user, trip) : throw new UnauthorizedAccessException();
    private static string IndexKey((Guid User, Guid Trip) scope) => $"personal-documents-{scope.User:N}-{scope.Trip:N}";
    private static string FileKey((Guid User, Guid Trip) scope, Guid id) => $"personal-document-file-{scope.User:N}-{scope.Trip:N}-{id:N}";

    public async Task<IReadOnlyList<LocalTripDocument>> ListAsync(CancellationToken ct = default)
    {
        var scope = Scope();
        var result = await cache.GetAsync<List<LocalTripDocument>>(IndexKey(scope), cancellationToken: ct);
        EnsureScope(scope);
        return result?.Value ?? [];
    }

    public async Task AttachAsync(Stream source, string name, CancellationToken ct = default)
    {
        if (!CanAttach) throw new UnauthorizedAccessException();
        var scope = Scope();
        var bytes = await LocalDocumentPolicy.ReadAsync(source, name, ct);
        await SaveAsync(scope, name, bytes, null, ct);
    }

    private async Task SaveAsync((Guid User, Guid Trip) scope, string name, byte[] bytes, string? url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            EnsureScope(scope);
            if (_deletedAccounts.Contains(scope.User) || _deletedTrips.Contains(scope))
                throw new OperationCanceledException("The document owner was deleted.");
            if (url is null ? !CanAttach : !sessions.HasCuratedDocs || !sessions.HasKnownValidAccess)
                throw new UnauthorizedAccessException();
            var index = (await cache.GetAsync<List<LocalTripDocument>>(IndexKey(scope), cancellationToken: ct))?.Value ?? [];
            var old = url is null ? null : index.FirstOrDefault(item => item.SourceUrl == url);
            var document = new LocalTripDocument(Guid.NewGuid(), Path.GetFileNameWithoutExtension(name),
                LocalDocumentPolicy.Extension(name), bytes.Length, DateTimeOffset.UtcNow, url);
            await cache.SaveAsync(FileKey(scope, document.Id), new LocalDocumentPayload(bytes), ct);
            try
            {
                EnsureScope(scope);
                await cache.SaveAsync(IndexKey(scope), index.Where(item => item.Id != old?.Id).Append(document).ToList(), ct);
            }
            catch { await cache.DeleteAsync(FileKey(scope, document.Id)); throw; }
            if (old is not null) await cache.DeleteAsync(FileKey(scope, old.Id));
        }
        finally { _gate.Release(); }
    }

    public async Task RenameAsync(Guid id, string title, CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 120) throw new ArgumentException("Title must contain 1–120 characters.");
        var scope = Scope();
        await _gate.WaitAsync(ct);
        try
        {
            var index = await ListAsync(ct);
            EnsureScope(scope);
            await cache.SaveAsync(IndexKey(scope), index.Select(item => item.Id == id ? item with { Title = title } : item).ToList(), ct);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var scope = Scope();
        await _gate.WaitAsync(ct);
        try
        {
            var index = await ListAsync(ct);
            EnsureScope(scope);
            await cache.SaveAsync(IndexKey(scope), index.Where(item => item.Id != id).ToList(), ct);
            await cache.DeleteAsync(FileKey(scope, id));
        }
        finally { _gate.Release(); }
    }

    public async Task OpenAsync(Guid id, CancellationToken ct = default)
    {
        var scope = Scope();
        var document = (await ListAsync(ct)).Single(item => item.Id == id);
        var payload = await cache.GetAsync<LocalDocumentPayload>(FileKey(scope, id), cancellationToken: ct)
            ?? throw new FileNotFoundException();
        EnsureScope(scope);
        var directory = Path.Combine(FileSystem.CacheDirectory, "document-preview");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id.ToString("N") + document.Extension);
        try
        {
            await File.WriteAllBytesAsync(path, payload.Value.Bytes, ct);
            EnsureScope(scope);
            if (!await Launcher.OpenAsync(new OpenFileRequest(document.Title, new ReadOnlyFile(path))))
                throw new IOException("No compatible viewer is installed.");
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public async Task<bool> IsDownloadedAsync(string url, CancellationToken ct = default)
    {
        var scope = Scope();
        var document = (await ListAsync(ct)).FirstOrDefault(item => item.SourceUrl == url);
        return document is not null && await cache.GetAsync<LocalDocumentPayload>(FileKey(scope, document.Id), cancellationToken: ct) is not null;
    }

    public async Task DownloadAsync(string url, string title, CancellationToken ct = default)
    {
        if (!sessions.HasCuratedDocs || !sessions.HasKnownValidAccess) throw new UnauthorizedAccessException();
        var scope = Scope();
        var uri = new Uri(api.BaseAddress!, url);
        for (var redirect = 0; redirect < 4; redirect++)
        {
            var sameOrigin = api.BaseAddress is { } origin && uri.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority);
            if (uri.Scheme != "https" && !(sameOrigin && uri.Scheme == "http")) throw new InvalidDataException("Unsupported document URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (sameOrigin) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await sessions.GetTokenAsync());
            using var response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            { uri = new Uri(uri, location); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > LocalDocumentPolicy.MaximumBytes) throw new InvalidDataException("File exceeds 20 MB.");
            var extension = response.Content.Headers.ContentType?.MediaType switch
            {
                "application/pdf" => ".pdf", "image/jpeg" => ".jpg", "image/png" => ".png",
                _ => LocalDocumentPolicy.Extension(uri.AbsolutePath)
            };
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var bytes = await LocalDocumentPolicy.ReadAsync(stream, "file" + extension, ct);
            await SaveAsync(scope, title + extension, bytes, url, ct);
            return;
        }
        throw new HttpRequestException("Too many document redirects.");
    }

    public static Task ClearPreviewsAsync()
    {
        var directory = Path.Combine(FileSystem.CacheDirectory, "document-preview");
        if (Directory.Exists(directory)) foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
        return Task.CompletedTask;
    }

    // Call only after the corresponding server deletion succeeds. Ordinary
    // logout never calls these methods, so personal attachments remain recoverable.
    public Task DeleteTripAsync(Guid userId, Guid tripId) => DeleteScopeAsync(userId, tripId);
    public Task DeleteAccountAsync(Guid userId) => DeleteScopeAsync(userId, null);

    private async Task DeleteScopeAsync(Guid userId, Guid? tripId)
    {
        if (userId == Guid.Empty || tripId == Guid.Empty) throw new ArgumentException("A valid owner is required.");
        var suffix = tripId.HasValue ? $"{userId:N}-{tripId.Value:N}" : $"{userId:N}-";
        await _gate.WaitAsync();
        try
        {
            if (tripId.HasValue) _deletedTrips.Add((userId, tripId.Value));
            else _deletedAccounts.Add(userId);
            await cache.DeleteByPrefixAsync($"personal-document-file-{suffix}", $"personal-documents-{suffix}",
                $"offline-preparation-{suffix}");
            await ClearPreviewsAsync();
        }
        finally { _gate.Release(); }
    }

    private void EnsureScope((Guid User, Guid Trip) expected)
    {
        if (Scope() != expected) throw new OperationCanceledException("The active trip changed.");
    }
}
