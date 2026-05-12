using TravelCompanion.Shared;

namespace TravelCompanion.Api.Models;

public sealed class UserEntitlement
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public ContentAccessLevel AccessLevel { get; set; }
    public Guid? DestinationId { get; set; }
    public Destination? Destination { get; set; }
    public Guid? TravelPackageId { get; set; }
    public TravelPackage? TravelPackage { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public required string Source { get; set; }
}
