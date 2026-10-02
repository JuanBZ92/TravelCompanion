using Microsoft.Extensions.Logging;
using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class DatabaseOperationTests
{
    [Fact]
    public async Task Nested_operations_accumulate_commands_and_do_not_leak_between_requests()
    {
        var logger = new RecordingLogger();
        async Task Request(string name)
        {
            using var operation = DatabaseOperation.Begin(name, logger);
            await Task.Yield();
            DatabaseOperation.Connection(TimeSpan.FromMilliseconds(3));
            DatabaseOperation.Command(TimeSpan.FromMilliseconds(5));
            using (var batch = DatabaseOperation.Begin("batch", logger))
            {
                DatabaseOperation.Command(TimeSpan.FromMilliseconds(7));
                batch.Rows = 25;
            }
        }
        await Task.WhenAll(Request("first"), Request("second"));
        Assert.Equal(4, logger.Entries.Count);
        foreach (var entry in logger.Entries.Where(e => (string)e["Operation"]! != "batch"))
        {
            Assert.Equal(2, entry["Commands"]);
            Assert.Equal(12d, entry["CommandMs"]);
            Assert.Equal(3d, entry["ConnectionMs"]);
            Assert.True((double)entry["ProcessAgeSeconds"]! >= 0);
        }
        using (DatabaseOperation.Begin("next", logger)) { }
        Assert.Equal(0, logger.Entries.Last()["Commands"]);
    }
    private sealed class RecordingLogger : ILogger
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
        }
    }
}
