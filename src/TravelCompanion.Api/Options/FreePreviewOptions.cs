namespace TravelCompanion.Api.Options;

public sealed class FreePreviewOptions
{
    public const string SectionName = "FreePreview";
    public const string ReservedPin = "0000";

    public bool Enabled { get; set; } = true;
    public string Pin { get; set; } = ReservedPin;
    public int SessionLifetimeDays { get; set; } = 7;
    public int TrialEditingMinutes { get; set; } = 30;
    public int DraftRetentionDays { get; set; } = 7;
    public int AssistantRequestLimit { get; set; } = 3;
    public decimal PassPrice { get; set; } = 24.99m;
    public string Currency { get; set; } = "EUR";
    public string? PurchaseUrl { get; set; }
    public string? MarkerObfuscationKey { get; set; }
}
