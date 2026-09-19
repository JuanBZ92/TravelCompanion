namespace TravelCompanion.Api.Services;

public static class MobileDataVersionScopes
{
    public static string Catalog(Guid destinationId) => $"catalog:{destinationId:N}";
    public static string FreeCatalog(Guid destinationId) => $"free-catalog:{destinationId:N}";
    public const string FreeCatalogGlobal = "free-catalog:global";
    public static string Documents(Guid tripId) => $"documents:{tripId:N}";
    public static string Today(Guid userId) => $"today:{userId:N}";
}
