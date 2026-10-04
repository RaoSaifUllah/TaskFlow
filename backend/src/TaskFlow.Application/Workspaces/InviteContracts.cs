using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Workspaces;

public sealed record InviteGrant(string Token, DateTimeOffset ExpiresAt);
public sealed record InviteRegistration(string Token, string Email, string Password, string TimeZone);
public interface IInviteService
{
    Task<InviteGrant> CreateAsync(RequestActor actor, Guid workspaceId, string email, WorkspaceRole role, CancellationToken ct);
    Task<bool> RegisterAsync(InviteRegistration request, CancellationToken ct);
    Task<bool> AcceptAsync(RequestActor actor, string token, CancellationToken ct);
}
