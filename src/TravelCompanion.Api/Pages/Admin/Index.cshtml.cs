using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class IndexModel(TravelCompanionDbContext dbContext) : PageModel
{
    public int DestinationCount { get; private set; }
    public int PackageCount { get; private set; }
    public int RecommendationCount { get; private set; }
    public int ReservationCount { get; private set; }
    public int DocumentCount { get; private set; }
    public int UserCount { get; private set; }
    public int EntitlementCount { get; private set; }

    public async Task OnGetAsync()
    {
        DestinationCount = await dbContext.Destinations.CountAsync();
        PackageCount = await dbContext.TravelPackages.CountAsync();
        RecommendationCount = await dbContext.Recommendations.CountAsync();
        ReservationCount = await dbContext.Reservations.CountAsync();
        DocumentCount = await dbContext.TravelDocuments.CountAsync();
        UserCount = await dbContext.AppUsers.CountAsync();
        EntitlementCount = await dbContext.UserEntitlements.CountAsync();
    }
}
