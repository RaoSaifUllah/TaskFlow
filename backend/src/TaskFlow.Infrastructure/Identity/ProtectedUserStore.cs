using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Identity;

public sealed class ProtectedUserStore(TaskFlowDbContext context, IDataProtectionProvider protection, IdentityErrorDescriber errors)
    : UserStore<TaskFlowUser, IdentityRole<Guid>, TaskFlowDbContext, Guid>(context, errors)
{
    private readonly IDataProtector protector = protection.CreateProtector("TaskFlow.MfaAuthenticatorKeys.v1");
    public override Task SetAuthenticatorKeyAsync(TaskFlowUser user, string key, CancellationToken cancellationToken) =>
        base.SetAuthenticatorKeyAsync(user, protector.CreateProtector(user.Id.ToString("N")).Protect(key), cancellationToken);
    public override async Task<string?> GetAuthenticatorKeyAsync(TaskFlowUser user, CancellationToken cancellationToken)
    {
        var stored = await base.GetAuthenticatorKeyAsync(user, cancellationToken);
        return stored is null ? null : protector.CreateProtector(user.Id.ToString("N")).Unprotect(stored);
    }
}
