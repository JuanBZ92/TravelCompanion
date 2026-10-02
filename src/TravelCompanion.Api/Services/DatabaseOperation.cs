using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TravelCompanion.Api.Services;

// Operation names are constants at call sites; never use user or trip identifiers here.
internal sealed class DatabaseOperation : IDisposable
{
    private static readonly AsyncLocal<DatabaseOperation?> Current = new();
    private static readonly Meter Meter = new("TravelCompanion.Database");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("database.operation.duration", "ms");
    private static readonly DateTime ProcessStarted = GetProcessStart();
    private static DateTime GetProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }
    private readonly DatabaseOperation? parent = Current.Value;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly ILogger? logger;
    private readonly string name;
    private int commands;
    private double commandMs;
    private double connectionMs;
    public int Rows { get; set; }

    private DatabaseOperation(string name, ILogger? logger)
    {
        this.name = name;
        this.logger = logger;
        Current.Value = this;
    }

    public static DatabaseOperation Begin(string name, ILogger? logger = null) => new(name, logger);
    public static void Command(TimeSpan duration)
    {
        for (var operation = Current.Value; operation is not null; operation = operation.parent)
        {
            operation.commands++;
            operation.commandMs += duration.TotalMilliseconds;
        }
    }
    public static void Connection(TimeSpan duration)
    {
        for (var operation = Current.Value; operation is not null; operation = operation.parent)
            operation.connectionMs += duration.TotalMilliseconds;
    }
    public void Dispose()
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Duration.Record(elapsed, new KeyValuePair<string, object?>("operation", name));
        logger?.LogInformation("Database operation {Operation}: TotalMs={TotalMs}, Commands={Commands}, CommandMs={CommandMs}, ConnectionMs={ConnectionMs}, Rows={Rows}, ProcessAgeSeconds={ProcessAgeSeconds}",
            name, elapsed, commands, commandMs, connectionMs, Rows, (DateTime.UtcNow - ProcessStarted).TotalSeconds);
        Current.Value = parent;
    }
}
