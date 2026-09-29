using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public static class ExpenseActivitySelection
{
    public static ExpenseActivityDto[] ForDate(IEnumerable<ExpenseActivityDto> items, DateOnly date) =>
        items.Where(x => x.Date == date).OrderBy(x => x.Title).ToArray();
}
