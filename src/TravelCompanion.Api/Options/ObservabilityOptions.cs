namespace TravelCompanion.Api.Options;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public int SlowRequestThresholdMs { get; set; } = 1000;
    public int SlowDependencyThresholdMs { get; set; } = 500;
    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
}
