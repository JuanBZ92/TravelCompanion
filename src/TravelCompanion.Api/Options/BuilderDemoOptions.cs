namespace TravelCompanion.Api.Options;

public sealed class BuilderDemoOptions
{
    public const string SectionName = "BuilderDemo";
    public bool Enabled { get; set; }
    public string Pin { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Builder Demo";
}
