using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelCompanion.Mobile.Services;

// Deliberately excludes exception messages, source paths, request URLs and payloads.
public sealed class DiagnosticJournal(string directory, int maxBytes = 256 * 1024)
{
    private readonly object gate = new();
    private string Current => Path.Combine(directory, "current.jsonl");
    private string Previous => Path.Combine(directory, "previous.jsonl");

    public void Write(string eventName, DiagnosticDetails? details = null, Exception? exception = null)
    {
        try
        {
            var errors = new List<DiagnosticError>();
            for (var error = exception; error is not null && errors.Count < 4; error = error.InnerException)
            {
                var frames = new StackTrace(error, false).GetFrames().Take(16)
                    .Select(frame => frame.GetMethod())
                    .Select(method => $"{method?.DeclaringType?.FullName}.{method?.Name}").ToArray();
                errors.Add(new(error.GetType().FullName, error.HResult, frames));
            }
            var line = JsonSerializer.Serialize(new DiagnosticEntry(DateTimeOffset.UtcNow, eventName, details, errors),
                DiagnosticJsonContext.Default.DiagnosticEntry) + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            if (bytes.Length > maxBytes) return;
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(Current) && new FileInfo(Current).Length + bytes.Length > maxBytes)
                    File.Move(Current, Previous, true);
                using var file = new FileStream(Current, FileMode.Append, FileAccess.Write, FileShare.Read);
                file.Write(bytes);
            }
        }
        catch { /* Diagnostics must never interrupt the app, including on a full disk. */ }
    }

    public string Snapshot()
    {
        lock (gate)
        {
            return (File.Exists(Previous) ? File.ReadAllText(Previous) : "")
                + (File.Exists(Current) ? File.ReadAllText(Current) : "");
        }
    }
}

public sealed record DiagnosticDetails
{
    public string? Version { get; init; }
    public string? Build { get; init; }
    public string? Platform { get; init; }
    public string? Os { get; init; }
    public string? Model { get; init; }
    public string? Page { get; init; }
    public string? Area { get; init; }
    public string? Level { get; init; }
    public int? EventId { get; init; }
    public int? Status { get; init; }
    public long? ElapsedMs { get; init; }
    public bool? Canceled { get; init; }
    public bool? IsTerminating { get; init; }
}

internal sealed record DiagnosticError(string? Type, int HResult, string[] Frames);
internal sealed record DiagnosticEntry(DateTimeOffset Utc, string EventName, DiagnosticDetails? Details, List<DiagnosticError> Errors);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagnosticEntry))]
internal partial class DiagnosticJsonContext : JsonSerializerContext;
