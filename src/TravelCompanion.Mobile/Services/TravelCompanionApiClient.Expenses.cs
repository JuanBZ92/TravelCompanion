using System.Net;
using System.Net.Http.Json;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.Services;

public sealed partial class TravelCompanionApiClient
{
    public async Task<ExpensesDto> GetExpensesAsync(string token, Guid trip, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{trip}/expenses", token);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpensesDto>(JsonOptions, ct))!;
    }
    public async Task<SaveExpenseResult> SaveExpenseAsync(string token, Guid trip, Guid id, SaveExpenseRequest value, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Put, $"api/mobile/trips/{trip}/expenses/{id}", token);
        request.Content = JsonContent.Create(value, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveExpenseResult>(JsonOptions, ct))!;
    }
    public async Task<ExpenseSettingsDto?> SaveExpenseSettingsAsync(string token, Guid trip, SaveExpenseSettingsRequest value, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Put, $"api/mobile/trips/{trip}/expenses/settings", token);
        request.Content = JsonContent.Create(value, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Conflict) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ExpenseSettingsDto>(JsonOptions, ct);
    }
    public async Task<ExpenseRateDto?> GetExpenseRateAsync(string token, Guid trip, string currency, string target, DateOnly date, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{trip}/expenses/rate?currency={currency}&target={target}&date={date:yyyy-MM-dd}", token);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<ExpenseRateDto>(JsonOptions, ct);
    }
    public async Task<ExpenseBreakdownDto> GetExpenseBreakdownAsync(string token, Guid trip, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{trip}/expenses/breakdown", token);
        using var response = await _httpClient.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExpenseBreakdownDto>(JsonOptions, ct))!;
    }
    public async Task<byte[]> ExportExpensesAsync(string token, Guid trip, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"api/mobile/trips/{trip}/expenses/export", token);
        using var response = await _httpClient.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
