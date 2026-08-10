namespace TravelCompanion.Api.Options;

public sealed class FreePreviewOptions
{
    public const string SectionName = "FreePreview";
    public const string ReservedPin = "0000";

    public bool Enabled { get; set; } = true;
    public string Pin { get; set; } = ReservedPin;
    public int SessionLifetimeDays { get; set; } = 7;
    public string? MarkerObfuscationKey { get; set; }
}
