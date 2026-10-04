using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests;

public sealed class InviteTests
{
    [PostgreSqlFact]
    public async Task InvitationRegistrationIsSingleUseAndRevocationExpiryAndEmailAreEnforced()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:Write"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_WRITE"),
            ["ConnectionStrings:Read"] = Environment.GetEnvironmentVariable("TASKFLOW_TEST_READ"),
            ["Auth:DataProtectionKeyDirectory"] = Path.Combine(AppContext.BaseDirectory,".local","invite-protection")
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(config); services.AddTaskFlow(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskFlowDbContext>(); await db.Database.MigrateAsync();
        var owner = new TaskFlowUser { Id = Guid.NewGuid(), Email = Guid.NewGuid()+"@example.invalid", SecurityStamp = Guid.NewGuid().ToString(), TwoFactorEnabled = true };
        var session = new RefreshSession { UserId = owner.Id, SecurityStamp = owner.SecurityStamp, MfaVerifiedAt = DateTimeOffset.UtcNow,
            IdleExpiresAt = DateTimeOffset.UtcNow.AddDays(7), AbsoluteExpiresAt = DateTimeOffset.UtcNow.AddDays(30) };
        var workspace = new Workspace("Invitation fixture");
        db.AddRange(owner, session, workspace, new Membership { WorkspaceId = workspace.Id, UserId = owner.Id, Role = WorkspaceRole.Owner });
        await db.SaveChangesAsync();
        var actor = new RequestActor(owner.Id, session.Id);
        var invites = scope.ServiceProvider.GetRequiredService<IInviteService>();
        var email = Guid.NewGuid()+"@example.invalid";
        var grant = await invites.CreateAsync(actor, workspace.Id, email, WorkspaceRole.Member, default);
        Assert.DoesNotContain(grant.Token, (await db.WorkspaceInvites.SingleAsync(x=>x.TokenHash==AuthService.Hash(grant.Token))).TokenHash);
        Assert.False(await invites.RegisterAsync(new InviteRegistration(grant.Token,"wrong@example.invalid","synthetic invite passphrase","UTC"),default));
        async Task<bool> Register()
        {
            await using var concurrent = provider.CreateAsyncScope();
            return await concurrent.ServiceProvider.GetRequiredService<IInviteService>().RegisterAsync(new InviteRegistration(grant.Token,email,"synthetic invite passphrase","UTC"),default);
        }
        var results = await Task.WhenAll(Register(),Register());
        Assert.Single(results,x=>x); Assert.Single(results,x=>!x);
        var registered = await db.Users.AsNoTracking().SingleAsync(x=>x.Email==email);
        Assert.True(registered.EmailConfirmed); Assert.NotNull(registered.PasswordHash);
        Assert.Equal(WorkspaceRole.Member,(await db.Memberships.AsNoTracking().SingleAsync(x=>x.WorkspaceId==workspace.Id && x.UserId==registered.Id)).Role);
        Assert.False(await Register());
        var expires = await invites.CreateAsync(actor,workspace.Id,Guid.NewGuid()+"@example.invalid",WorkspaceRole.Guest,default);
        await db.WorkspaceInvites.Where(x=>x.TokenHash==AuthService.Hash(expires.Token)).ExecuteUpdateAsync(x=>x.SetProperty(i=>i.ExpiresAt,DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.False(await invites.AcceptAsync(actor,expires.Token,default));
        var withdrawnEmail = Guid.NewGuid()+"@example.invalid";
        var withdrawn = await invites.CreateAsync(actor,workspace.Id,withdrawnEmail,WorkspaceRole.Guest,default);
        await db.Memberships.Where(x=>x.WorkspaceId==workspace.Id && x.UserId==owner.Id).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.Active,false));
        Assert.False(await invites.RegisterAsync(new InviteRegistration(withdrawn.Token,withdrawnEmail,"synthetic invite passphrase","UTC"),default));
    }
}
