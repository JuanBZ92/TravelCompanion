namespace TravelCompanion.Api.Options;

public sealed class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 10;
}
