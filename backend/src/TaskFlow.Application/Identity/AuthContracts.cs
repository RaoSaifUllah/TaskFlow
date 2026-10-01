namespace TaskFlow.Application.Identity;

public sealed record AuthUser(Guid Id, string Email, string TimeZone, bool InstanceAdmin, bool MfaEnabled);
public sealed record AuthGrant(string AccessToken, string RefreshToken, string CsrfToken, DateTimeOffset ExpiresAt, AuthUser User);
public sealed record SessionSummary(Guid Id, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);
public interface IAuthService
{
    Task<AuthGrant?> LoginAsync(string email, string password, string? totp, string device, CancellationToken cancellationToken);
    Task<AuthGrant?> RotateAsync(string token, string csrf, CancellationToken cancellationToken);
    Task RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<bool> ValidateSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionSummary>> SessionsAsync(Guid userId, Guid currentSession, CancellationToken cancellationToken);
}
