namespace TaskFlow.Application.Identity;

public enum AccountChallengePurpose { ResetPassword, VerifyEmail }
public sealed record AccountMessage(string Email, string Token, AccountChallengePurpose Purpose);
public interface IAccountMessageSink
{
    Task DeliverAsync(AccountMessage message, CancellationToken ct);
}
public interface IAccountLifecycle
{
    Task RequestResetAsync(string email, CancellationToken ct);
    Task RequestVerificationAsync(Guid userId, CancellationToken ct);
    Task<bool> ResetAsync(string token, string password, CancellationToken ct);
    Task<bool> VerifyAsync(string token, CancellationToken ct);
}
