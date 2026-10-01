namespace TaskFlow.Application.Identity;

public sealed record BootstrapRequest(string Email, string Password, string Token, string WorkspaceName, string TimeZone);
public interface IAccountService
{
    Task<bool> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken);
    Task<string?> BeginMfaAsync(Guid userId, Guid sessionId, string password, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>?> ConfirmMfaAsync(Guid userId, string code, CancellationToken cancellationToken);
    Task<bool> StepUpAsync(Guid userId, Guid sessionId, string code, CancellationToken cancellationToken);
}
