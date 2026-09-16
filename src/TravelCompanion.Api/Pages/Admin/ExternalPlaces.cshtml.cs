using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Pages.Admin;

public sealed class ExternalPlacesModel(ExternalPlaceInsightsService insightsService) : PageModel
{
    public ExternalPlaceInsightsReport Report { get; private set; } =
        new(0, 0, 0, 0, []);

    public IReadOnlyList<ExternalPlaceInsight> Places { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "pending";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await insightsService.GetReportAsync(cancellationToken);
        IEnumerable<ExternalPlaceInsight> places = Report.Places;

        places = Status.ToLowerInvariant() switch
        {
            "catalog" => places.Where(item => item.IsInCatalog),
            "all" => places,
            _ => places.Where(item => !item.IsInCatalog)
        };

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            places = places.Where(item =>
                Contains(item.Title, term)
                || Contains(item.City, term)
                || Contains(item.DestinationName, term)
                || Contains(item.ProviderPlaceId, term)
                || item.TripNames.Any(trip => Contains(trip, term)));
        }

        Places = places.ToList();
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
