using TravelCompanion.Api.Models;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface IItineraryService
{
    Task<SaveItineraryItemResponse> SaveItineraryItemAsync(
        AppUser user,
        SaveItineraryItemRequest request,
        CancellationToken cancellationToken);
}
