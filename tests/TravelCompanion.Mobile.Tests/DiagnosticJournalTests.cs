using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Tests;

public sealed class DiagnosticJournalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "yuku-diagnostics-" + Guid.NewGuid());

    [Fact]
    public void PreservesHistoryAcrossInstancesWithoutExceptionMessagesOrData()
    {
        var error = new InvalidOperationException("PIN 123456 token secret", new Exception("personal note"));
        error.Data["email"] = "private@example.com";
        new DiagnosticJournal(directory).Write("unhandled_exception", exception: error);
        var history = new DiagnosticJournal(directory).Snapshot();
        Assert.Contains(nameof(InvalidOperationException), history);
        Assert.DoesNotContain("123456", history);
        Assert.DoesNotContain("secret", history);
        Assert.DoesNotContain("personal note", history);
        Assert.DoesNotContain("private@example.com", history);
    }

    [Fact]
    public void ConcurrentWritesRotateAndRemainValidJsonWithinSizeLimit()
    {
        var journal = new DiagnosticJournal(directory, 1024);
        Parallel.For(0, 200, index => journal.Write("http_completed", new() { Status = 200, ElapsedMs = index }));
        Assert.InRange(Directory.GetFiles(directory).Length, 1, 2);
        Assert.All(Directory.GetFiles(directory), file => Assert.True(new FileInfo(file).Length <= 1024));
        foreach (var line in journal.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var json = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal("http_completed", json.RootElement.GetProperty("eventName").GetString());
        }
    }

    [Fact]
    public void StorageFailureDoesNotThrow()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(path, "");
        new DiagnosticJournal(path).Write("process_start");
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
