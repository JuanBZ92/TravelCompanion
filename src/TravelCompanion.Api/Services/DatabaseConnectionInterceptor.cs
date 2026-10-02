using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelCompanion.Api.Services;

public sealed class DatabaseConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        DatabaseOperation.Connection(eventData.Duration);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        DatabaseOperation.Connection(eventData.Duration);
        return Task.CompletedTask;
    }
}
