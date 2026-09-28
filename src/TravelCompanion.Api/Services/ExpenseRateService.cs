using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Services;

public interface IExpenseRateService
{
    Task<ExpenseRateDto?> GetAsync(string currency, string target, DateOnly date, CancellationToken ct);
}

public sealed class ExpenseRateService(HttpClient http, IMemoryCache cache) : IExpenseRateService
{
    public async Task<ExpenseRateDto?> GetAsync(string currency, string target, DateOnly date, CancellationToken ct)
    {
        if (!ExpensePolicy.Currencies.Contains(currency) || !ExpensePolicy.Currencies.Contains(target))
            throw new ArgumentException("Moneda no disponible.");
        if (currency == target) return new(currency, target, 1, date, "identity");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today) date = today;
        var key = $"expenses-rate:{currency}:{target}:{date}";
        if (cache.TryGetValue<ExpenseRateDto>(key, out var existing)) return existing;
        try
        {
            // Only currency codes and dates reach the provider, never amounts or user data.
            var rate = await http.GetFromJsonAsync<ProviderRate>($"v2/rate/{currency}/{target}?date={date:yyyy-MM-dd}", ct);
            if (rate is null || rate.Rate <= 0 || rate.Rate > 100000000m || rate.Date > date) return null;
            var result = new ExpenseRateDto(currency, target, rate.Rate, rate.Date, "Frankfurter");
            cache.Set(key, result, TimeSpan.FromHours(24));
            return result;
        }
        catch (HttpRequestException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }
    private sealed record ProviderRate(decimal Rate, DateOnly Date);
}
