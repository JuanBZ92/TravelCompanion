namespace TravelCompanion.Api.Models;

public sealed class AppUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public List<UserEntitlement> Entitlements { get; set; } = [];
}
