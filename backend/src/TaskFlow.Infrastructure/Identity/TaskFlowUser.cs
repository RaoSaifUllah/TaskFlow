using Microsoft.AspNetCore.Identity;

namespace TaskFlow.Infrastructure.Identity;

public sealed class TaskFlowUser : IdentityUser<Guid>
{
    public string TimeZone { get; set; } = "UTC";
    public bool Active { get; set; } = true;
    public bool InstanceAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string CsrfHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? MfaVerifiedAt { get; set; }
}
public sealed class RefreshToken
{
    public string Hash { get; set; } = "";
    public Guid SessionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
