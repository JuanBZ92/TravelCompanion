using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TravelCompanion.Api.Data;

public static class TripConcurrencyLock
{
    public static async Task LockAsync(TravelCompanionDbContext dbContext, Guid tripId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null
            || dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
            return;

        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "SELECT 1 FROM \"Trips\" WHERE \"Id\" = @tripId FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tripId";
        parameter.Value = tripId;
        command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            throw new KeyNotFoundException("El viaje ya no existe.");
    }
}
