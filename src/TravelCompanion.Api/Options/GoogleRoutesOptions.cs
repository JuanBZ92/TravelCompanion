namespace TravelCompanion.Api.Options;

public sealed class GoogleRoutesOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
}
