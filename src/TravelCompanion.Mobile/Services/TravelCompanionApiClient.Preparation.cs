using System.Net.Http.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed partial class TravelCompanionApiClient
{
    public async Task<List<TripPreparationItemDto>?> GetPreparationAsync(string token, Guid tripId, CancellationToken ct)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, $"api/mobile/trips/{tripId}/preparation", token);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TripPreparationItemDto>>(JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<bool> SavePreparationAsync(string token, Guid tripId, TripPreparationItemDto item, CancellationToken ct)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Put, $"api/mobile/trips/{tripId}/preparation/{Uri.EscapeDataString(item.Key)}", token);
        request.Content = JsonContent.Create(new SaveTripPreparationItemRequest(!item.Completed, item.Revision), options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
