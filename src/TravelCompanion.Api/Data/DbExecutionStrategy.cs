using Microsoft.EntityFrameworkCore;

namespace TravelCompanion.Api.Data;

public static class DbExecutionStrategy
{
    private static readonly AsyncLocal<int> Depth = new();

    public static bool ShouldExecute(TravelCompanionDbContext dbContext) =>
        Depth.Value == 0 && dbContext.Database.IsRelational()
        && dbContext.Database.CurrentTransaction is null
        && dbContext.Database.CreateExecutionStrategy().RetriesOnFailure;

    public static Task<T> ExecuteAsync<T>(TravelCompanionDbContext dbContext, Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async _ =>
        {
            Depth.Value++;
            try
            {
                dbContext.ChangeTracker.Clear();
                return await operation();
            }
            finally
            {
                Depth.Value--;
            }
        }, cancellationToken);
    }

    public static Task ExecuteAsync(TravelCompanionDbContext dbContext, Func<Task> operation,
        CancellationToken cancellationToken) =>
        ExecuteAsync(dbContext, async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
}
