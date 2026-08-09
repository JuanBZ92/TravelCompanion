using System.Diagnostics;
using System.Diagnostics.Metrics;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public sealed class TravelAssistantTelemetry
{
    private static readonly Meter Meter = new("TravelCompanion.TravelAssistant", "1.0.0");
    private static readonly Counter<long> ChatRequests = Meter.CreateCounter<long>("travel_assistant.chat.requests");
    private static readonly Counter<long> MissingContext = Meter.CreateCounter<long>("travel_assistant.chat.missing_context");
    private static readonly Counter<long> ModelFallback = Meter.CreateCounter<long>("travel_assistant.chat.model_fallback");
    private static readonly Counter<long> CardsReturned = Meter.CreateCounter<long>("travel_assistant.chat.cards_returned");
    private static readonly Counter<long> RankingCandidates = Meter.CreateCounter<long>("travel_assistant.ranking.candidates");
    private static readonly Counter<long> FeedbackSignals = Meter.CreateCounter<long>("travel_assistant.feedback.signals");
    private static readonly Histogram<double> ChatLatency = Meter.CreateHistogram<double>("travel_assistant.chat.duration_ms");

    private readonly ILogger<TravelAssistantTelemetry> _logger;

    public TravelAssistantTelemetry(ILogger<TravelAssistantTelemetry> logger)
    {
        _logger = logger;
    }

    public ChatRequestTiming StartChatRequest(string? locale, string? promptVersion)
    {
        ChatRequests.Add(1, CreateTags(locale, promptVersion));
        return new ChatRequestTiming(this, Stopwatch.StartNew(), locale, promptVersion);
    }

    public void RecordChatOutcome(
        TravelChatResponse response,
        string? responseMode,
        bool usedModelResponse,
        string? eventName,
        string? locale,
        string? promptVersion,
        TravelAssistantDiagnostics? diagnostics)
    {
        var tags = CreateTags(locale, promptVersion);
        tags.Add("intent", response.Intent);
        tags.Add("response_mode", responseMode ?? "none");
        tags.Add("event", eventName ?? "response");

        if (response.MissingContext is not null)
        {
            MissingContext.Add(1, tags);
        }

        if (!usedModelResponse)
        {
            ModelFallback.Add(1, tags);
        }

        CardsReturned.Add(response.Cards.Count, tags);
        if (diagnostics is not null)
        {
            RankingCandidates.Add(diagnostics.RankedCandidates, tags);
        }

        _logger.LogInformation(
            "Travel assistant telemetry. Event={EventName}; Intent={Intent}; ResponseMode={ResponseMode}; Locale={Locale}; PromptVersion={PromptVersion}; MissingContext={MissingContext}; UsedModelResponse={UsedModelResponse}; Cards={Cards}; RankedCandidates={RankedCandidates}.",
            eventName ?? "response",
            response.Intent,
            responseMode ?? "none",
            locale ?? "none",
            promptVersion ?? "none",
            response.MissingContext?.Field ?? "none",
            usedModelResponse,
            response.Cards.Count,
            diagnostics?.RankedCandidates);
    }

    public void RecordFeedback(
        TravelAssistantFeedbackSignal signal,
        bool accepted,
        string? locale,
        string? intent,
        string? responseMode)
    {
        FeedbackSignals.Add(
            1,
            new KeyValuePair<string, object?>("signal", signal.ToString()),
            new KeyValuePair<string, object?>("accepted", accepted),
            new KeyValuePair<string, object?>("locale", locale ?? "none"),
            new KeyValuePair<string, object?>("intent", intent ?? "none"),
            new KeyValuePair<string, object?>("response_mode", responseMode ?? "none"));
    }

    private void RecordLatency(double elapsedMs, string? locale, string? promptVersion)
    {
        ChatLatency.Record(elapsedMs, CreateTags(locale, promptVersion));
    }

    private static TagList CreateTags(string? locale, string? promptVersion)
    {
        var tags = new TagList
        {
            { "locale", locale ?? "none" },
            { "prompt_version", promptVersion ?? "none" }
        };
        return tags;
    }

    public sealed class ChatRequestTiming : IDisposable
    {
        private readonly TravelAssistantTelemetry _telemetry;
        private readonly Stopwatch _stopwatch;
        private readonly string? _locale;
        private readonly string? _promptVersion;
        private bool _disposed;

        internal ChatRequestTiming(
            TravelAssistantTelemetry telemetry,
            Stopwatch stopwatch,
            string? locale,
            string? promptVersion)
        {
            _telemetry = telemetry;
            _stopwatch = stopwatch;
            _locale = locale;
            _promptVersion = promptVersion;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            _telemetry.RecordLatency(_stopwatch.Elapsed.TotalMilliseconds, _locale, _promptVersion);
        }
    }
}
