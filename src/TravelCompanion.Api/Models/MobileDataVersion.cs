namespace TravelCompanion.Api.Models;

public sealed class MobileDataVersion
{
    public required string Scope { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
