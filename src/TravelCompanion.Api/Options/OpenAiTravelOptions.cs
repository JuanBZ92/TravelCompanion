namespace TravelCompanion.Api.Options;

public sealed class OpenAiTravelOptions
{
    public const string SectionName = "OpenAI";

    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gpt-4o-mini";
    public string? ApiKey { get; set; }
    public int MaxOutputTokenCount { get; set; } = 500;
    public string PromptVersion { get; set; } = "travel-chat.v1";
}
