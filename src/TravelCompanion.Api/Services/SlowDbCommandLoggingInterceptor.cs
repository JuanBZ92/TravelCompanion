using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using TravelCompanion.Api.Options;

namespace TravelCompanion.Api.Services;

public sealed class SlowDbCommandLoggingInterceptor(
    ILogger<SlowDbCommandLoggingInterceptor> logger,
    IOptions<ObservabilityOptions> options) : DbCommandInterceptor
{
    private readonly int _slowDependencyThresholdMs = Math.Max(1, options.Value.SlowDependencyThresholdMs);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        LogSlowDependency(command, eventData);
        return result;
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        LogSlowDependency(command, eventData);
        return result;
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LogSlowDependency(command, eventData);
        return result;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        logger.LogError(
            eventData.Exception,
            "Database dependency failed after {ElapsedMs}ms. CommandType={CommandType}. Sql={SqlSnippet}",
            eventData.Duration.TotalMilliseconds,
            command.CommandType,
            ToSqlSnippet(command.CommandText));
    }

    private void LogSlowDependency(DbCommand command, CommandExecutedEventData eventData)
    {
        var elapsedMs = eventData.Duration.TotalMilliseconds;
        if (elapsedMs < _slowDependencyThresholdMs)
        {
            return;
        }

        logger.LogWarning(
            "Slow database dependency detected: {ElapsedMs}ms (threshold {ThresholdMs}ms). CommandType={CommandType}. Sql={SqlSnippet}",
            elapsedMs,
            _slowDependencyThresholdMs,
            command.CommandType,
            ToSqlSnippet(command.CommandText));
    }

    private static string ToSqlSnippet(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var normalized = sql.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 240
            ? normalized
            : $"{normalized[..240]}...";
    }
}
