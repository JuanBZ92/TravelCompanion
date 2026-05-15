using System.Diagnostics;
using Microsoft.Extensions.Logging;

#if ANDROID
using Android.Util;
#endif

namespace TravelCompanion.Mobile.Services;

public sealed class MobileDiagnosticsLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new MobileDiagnosticsLogger(categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class MobileDiagnosticsLogger(string categoryName) : ILogger
    {
        private const string Tag = "TravelCompanion";

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var line = $"TCMOBILE {DateTimeOffset.Now:HH:mm:ss.fff} {logLevel}: {GetShortCategory(categoryName)} - {message}";
            Debug.WriteLine(line);

#if ANDROID
            WriteAndroidLog(logLevel, line);
            if (exception is not null)
            {
                Android.Util.Log.Error(Tag, exception.ToString());
            }
#endif
        }

        private static string GetShortCategory(string category)
        {
            var lastDotIndex = category.LastIndexOf('.');
            return lastDotIndex >= 0 && lastDotIndex < category.Length - 1
                ? category[(lastDotIndex + 1)..]
                : category;
        }

#if ANDROID
        private static void WriteAndroidLog(LogLevel logLevel, string line)
        {
            _ = logLevel switch
            {
                LogLevel.Trace => Android.Util.Log.Verbose(Tag, line),
                LogLevel.Debug => Android.Util.Log.Debug(Tag, line),
                LogLevel.Information => Android.Util.Log.Info(Tag, line),
                LogLevel.Warning => Android.Util.Log.Warn(Tag, line),
                LogLevel.Error => Android.Util.Log.Error(Tag, line),
                LogLevel.Critical => Android.Util.Log.Wtf(Tag, line),
                _ => Android.Util.Log.Info(Tag, line)
            };
        }
#endif
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
