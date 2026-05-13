using Microsoft.EntityFrameworkCore;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Api.Controllers;

internal readonly record struct PaginationRequest(int Page, int PageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static bool TryCreate(int page, int pageSize, out PaginationRequest request, out string? error)
    {
        request = default;

        if (page < 1)
        {
            error = "Page must be greater than or equal to 1.";
            return false;
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            error = $"Page size must be between 1 and {MaxPageSize}.";
            return false;
        }

        request = new PaginationRequest(page, pageSize);
        error = null;
        return true;
    }
}

internal static class PaginationExtensions
{
    public static async Task<PagedResultDto<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return Create(items, pagination, totalItems);
    }

    public static PagedResultDto<T> ToPagedResult<T>(
        this IReadOnlyList<T> items,
        PaginationRequest pagination,
        int totalItems)
    {
        return Create(items, pagination, totalItems);
    }

    private static PagedResultDto<T> Create<T>(
        IReadOnlyList<T> items,
        PaginationRequest pagination,
        int totalItems)
    {
        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pagination.PageSize);

        return new PagedResultDto<T>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalItems,
            totalPages,
            pagination.Page > 1,
            pagination.Page < totalPages);
    }
}
