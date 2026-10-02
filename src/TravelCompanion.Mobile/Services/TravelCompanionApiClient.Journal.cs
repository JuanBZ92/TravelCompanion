using System.Net;
using System.Net.Http.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed partial class TravelCompanionApiClient
{
    public async Task<List<JournalFreeEntryDto>> GetJournalFreeAsync(string token, Guid tripId, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{tripId}/journal/free-entries", token);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JournalFreeEntryDto>>(JsonOptions, ct) ?? [];
    }

    public Task<JournalFreeSaveResult> SaveJournalFreeAsync(string token, Guid tripId, Guid id, SaveJournalFreeEntryRequest value, CancellationToken ct) =>
        MutateJournalFreeAsync(token, tripId, id, HttpMethod.Put, value, ct);

    public Task<JournalFreeSaveResult> DeleteJournalFreeAsync(string token, Guid tripId, Guid id, DeleteJournalFreeEntryRequest value, CancellationToken ct) =>
        MutateJournalFreeAsync(token, tripId, id, HttpMethod.Delete, value, ct);

    private async Task<JournalFreeSaveResult> MutateJournalFreeAsync<T>(string token, Guid tripId, Guid id, HttpMethod method, T value, CancellationToken ct)
    {
        using var request = CreateRequest(method, $"api/mobile/trips/{tripId}/journal/free-entries/{id}", token);
        request.Content = JsonContent.Create(value, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JournalFreeSaveResult>(JsonOptions, ct))!;
    }
    public async Task<List<JournalNoteDto>> GetJournalAsync(string token, Guid tripId, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{tripId}/journal", token);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<JournalNoteDto>>(JsonOptions, ct) ?? [];
    }

    public async Task<JournalSaveResult> SaveJournalAsync(string token, Guid tripId, Guid activityId,
        SaveJournalNoteRequest value, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Put, $"api/mobile/trips/{tripId}/journal/{activityId}", token);
        request.Content = JsonContent.Create(value, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JournalSaveResult>(JsonOptions, ct))!;
    }
}
