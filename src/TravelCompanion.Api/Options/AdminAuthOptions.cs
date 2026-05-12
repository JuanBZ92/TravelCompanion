namespace TravelCompanion.Api.Options;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "travel-companion-dev";
}
