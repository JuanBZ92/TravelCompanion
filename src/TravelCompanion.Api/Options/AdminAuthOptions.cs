namespace TravelCompanion.Api.Options;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";
    public const string DevelopmentPassword = "travel-companion-dev";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = DevelopmentPassword;
}
