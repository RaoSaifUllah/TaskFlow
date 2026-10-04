using TaskFlow.Application.Workspaces;
using TaskFlow.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Application.Identity;
using TaskFlow.Application.Persistence;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTaskFlow(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TaskFlowDbContext>((provider, options) =>
        {
            var configured = provider.GetRequiredService<IConfiguration>().GetConnectionString("Write") ??
                throw new InvalidOperationException("ConnectionStrings:Write is required.");
            options.UseNpgsql(DatabaseConnections.Normalize(configured));
        });
        services.AddDataProtection().SetApplicationName("TaskFlow");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((options, config, logging) =>
        {
            var protectionPath = config["Auth:DataProtectionKeyDirectory"] ?? Path.Combine(".local", "dataprotection");
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(protectionPath), logging);
        });
        services.AddIdentityCore<TaskFlowUser>(o =>
        {
            o.Password.RequiredLength = 12;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireDigit = false;
            o.Password.RequireUppercase = false;
            o.Password.RequireLowercase = false;
            o.User.RequireUniqueEmail = true;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.Lockout.MaxFailedAccessAttempts = 5;
        }).AddEntityFrameworkStores<TaskFlowDbContext>().AddDefaultTokenProviders();
        services.AddScoped<IPasswordHasher<TaskFlowUser>, Argon2PasswordHasher>();
        services.AddScoped<IUserStore<TaskFlowUser>, ProtectedUserStore>();
        services.AddSingleton<JwtIssuer>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<AuthenticatorReplayGuard>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountLifecycle, AccountLifecycle>();
        services.AddSingleton<IAccountMessageSink, LocalAccountMessageSink>();
        services.AddScoped<IInviteService, InviteService>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IWorkspaceReadService>(provider => new WorkspaceReadService(provider.GetRequiredService<IConfiguration>().GetConnectionString("Read") ?? throw new InvalidOperationException("ConnectionStrings:Read is required.")));
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITaskReadService>(provider => new TaskReadService(provider.GetRequiredService<IConfiguration>()
            .GetConnectionString("Read") ?? throw new InvalidOperationException("ConnectionStrings:Read is required.")));
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}


