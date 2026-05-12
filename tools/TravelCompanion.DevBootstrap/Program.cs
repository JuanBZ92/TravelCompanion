using System.Diagnostics;
using System.Net.Sockets;

var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
if (solutionRoot is null)
{
    Console.Error.WriteLine("Could not find TravelCompanion.sln from the bootstrap output directory.");
    return 1;
}

Console.WriteLine("Starting PostgreSQL with Docker Compose...");
var dockerExitCode = await RunProcessAsync("docker", "compose up -d", solutionRoot);
if (dockerExitCode != 0)
{
    Console.Error.WriteLine("Docker Compose failed. Make sure Docker Desktop is running in Linux containers mode.");
    return dockerExitCode;
}

Console.WriteLine("Waiting for PostgreSQL on localhost:5432...");
var postgresReady = await WaitForTcpPortAsync("localhost", 5432, TimeSpan.FromSeconds(45));
if (!postgresReady)
{
    Console.Error.WriteLine("PostgreSQL did not become reachable within 45 seconds.");
    return 2;
}

Console.WriteLine("Development services are ready.");
return 0;

static string? FindSolutionRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "TravelCompanion.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory)
{
    using var process = Process.Start(new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    });

    if (process is null)
    {
        Console.Error.WriteLine($"Could not start '{fileName}'.");
        return 1;
    }

    var outputTask = PipeAsync(process.StandardOutput, Console.Out);
    var errorTask = PipeAsync(process.StandardError, Console.Error);

    await process.WaitForExitAsync();
    await Task.WhenAll(outputTask, errorTask);

    return process.ExitCode;
}

static async Task PipeAsync(TextReader reader, TextWriter writer)
{
    while (await reader.ReadLineAsync() is { } line)
    {
        writer.WriteLine(line);
    }
}

static async Task<bool> WaitForTcpPortAsync(string host, int port, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;

    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            return true;
        }
        catch (SocketException)
        {
            await Task.Delay(1000);
        }
    }

    return false;
}
