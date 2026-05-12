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

await ConfigureAndroidUsbReverseAsync();

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

static async Task ConfigureAndroidUsbReverseAsync()
{
    var adbPath = FindAdbPath();
    if (adbPath is null)
    {
        Console.WriteLine("ADB was not found. Skipping Android USB port reverse.");
        return;
    }

    var devicesOutput = await CaptureProcessAsync(adbPath, "devices", Environment.CurrentDirectory);
    var deviceSerials = devicesOutput
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Skip(1)
        .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2 && parts[1] == "device")
        .Select(parts => parts[0])
        .ToList();

    if (deviceSerials.Count == 0)
    {
        Console.WriteLine("No authorized Android USB devices found. Skipping Android USB port reverse.");
        return;
    }

    foreach (var serial in deviceSerials)
    {
        Console.WriteLine($"Configuring Android USB reverse for {serial}: tcp:5289 -> tcp:5289");
        var exitCode = await RunProcessAsync(adbPath, $"-s {serial} reverse tcp:5289 tcp:5289", Environment.CurrentDirectory);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"Could not configure adb reverse for {serial}.");
        }
    }
}

static string? FindAdbPath()
{
    var androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
        ?? Environment.GetEnvironmentVariable("ANDROID_HOME");

    if (!string.IsNullOrWhiteSpace(androidSdkRoot))
    {
        var sdkAdbPath = Path.Combine(androidSdkRoot, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        if (File.Exists(sdkAdbPath))
        {
            return sdkAdbPath;
        }
    }

    var defaultWindowsAdbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Android",
        "android-sdk",
        "platform-tools",
        "adb.exe");

    if (File.Exists(defaultWindowsAdbPath))
    {
        return defaultWindowsAdbPath;
    }

    return "adb";
}

static async Task<string> CaptureProcessAsync(string fileName, string arguments, string workingDirectory)
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
        return string.Empty;
    }

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        Console.Error.Write(await errorTask);
    }

    return await outputTask;
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
