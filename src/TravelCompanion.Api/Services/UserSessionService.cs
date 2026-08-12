using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TravelCompanion.Api.Data;
using TravelCompanion.Api.Models;
using TravelCompanion.Shared;

namespace TravelCompanion.Api.Services;

public sealed class UserSessionService(TravelCompanionDbContext dbContext)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan LastSeenUpdateInterval = TimeSpan.FromMinutes(15);
    private static readonly object SessionContextItemKey = new();

    public async Task<(AppUserSession Session, string Token)> CreateSessionAsync(
        AppUser user,
        CancellationToken cancellationToken = default,
        Guid? tripId = null,
        SessionAccessMode accessMode = SessionAccessMode.Trip,
        TimeSpan? lifetime = null)
    {
        var token = CreateToken();
        var now = DateTimeOffset.UtcNow;
        var session = new AppUserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TripId = tripId,
            AccessMode = accessMode,
            TokenHash = HashToken(token),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime ?? SessionLifetime),
            LastSeenAt = now
        };

        dbContext.AppUserSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (session, token);
    }

    public async Task<AppUser?> GetUserAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextAsync(httpContext, cancellationToken);
        return context?.User;
    }

    public async Task<Guid?> GetSessionTripIdAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextAsync(httpContext, cancellationToken);
        return context?.TripId;
    }

    public async Task<SessionAccessMode?> GetSessionAccessModeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextAsync(httpContext, cancellationToken);
        return context?.AccessMode;
    }

    public async Task<UserSessionContext?> GetSessionContextAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Items.TryGetValue(SessionContextItemKey, out var cached))
        {
            return cached as UserSessionContext;
        }

        var token = GetBearerToken(httpContext);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashToken(token);
        var now = DateTimeOffset.UtcNow;
        var session = await dbContext.AppUserSessions
            .AsNoTracking()
            .Include(existingSession => existingSession.User)
            .ThenInclude(user => user!.Entitlements)
            .FirstOrDefaultAsync(existingSession =>
                existingSession.TokenHash == tokenHash
                && existingSession.RevokedAt == null
                && existingSession.ExpiresAt > now,
                cancellationToken);

        if (session?.User is null)
        {
            return null;
        }

        if (!session.LastSeenAt.HasValue || now - session.LastSeenAt.Value >= LastSeenUpdateInterval)
        {
            await dbContext.AppUserSessions
                .Where(existingSession =>
                    existingSession.Id == session.Id
                    && (existingSession.LastSeenAt == null
                        || existingSession.LastSeenAt < now - LastSeenUpdateInterval))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(existingSession => existingSession.LastSeenAt, now),
                    cancellationToken);
        }

        var context = new UserSessionContext(
            session.Id,
            session.User,
            session.TripId,
            session.AccessMode,
            session.ExpiresAt);
        httpContext.Items[SessionContextItemKey] = context;
        return context;
    }

    public async Task RevokeCurrentSessionAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var token = GetBearerToken(httpContext);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var tokenHash = HashToken(token);
        var session = await dbContext.AppUserSessions
            .FirstOrDefaultAsync(existingSession =>
                existingSession.TokenHash == tokenHash
                && existingSession.RevokedAt == null,
                cancellationToken);

        if (session is null)
        {
            return;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BindCurrentSessionToTripAsync(
        HttpContext httpContext,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var context = await GetSessionContextAsync(httpContext, cancellationToken)
            ?? throw new InvalidOperationException("An authenticated session is required.");
        var session = await dbContext.AppUserSessions
            .FirstOrDefaultAsync(item => item.Id == context.SessionId && item.RevokedAt == null, cancellationToken)
            ?? throw new InvalidOperationException("The session is no longer active.");
        session.TripId = tripId;
        await dbContext.SaveChangesAsync(cancellationToken);
        httpContext.Items.Remove(SessionContextItemKey);
    }

    public async Task RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var sessions = await dbContext.AppUserSessions
            .Where(session => session.UserId == userId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetBearerToken(HttpContext httpContext)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : string.Empty;
    }

    private static string CreateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}

public sealed record UserSessionContext(
    Guid SessionId,
    AppUser User,
    Guid? TripId,
    SessionAccessMode AccessMode,
    DateTimeOffset ExpiresAt);
