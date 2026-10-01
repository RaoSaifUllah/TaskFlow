using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class TaskFlowDbContext(DbContextOptions<TaskFlowDbContext> options) : IdentityDbContext<TaskFlowUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectShare> ProjectShares => Set<ProjectShare>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<RecoveryCode>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Hash }); e.Property(x => x.Hash).HasMaxLength(64);
            e.HasOne<TaskFlowUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskFlowUser>(e => { e.Property(x => x.TimeZone).HasMaxLength(100); });
        model.Entity<RefreshSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DeviceName).HasMaxLength(120);
            e.Property(x => x.SecurityStamp).HasMaxLength(100);
            e.Property(x => x.CsrfHash).HasMaxLength(64);
            e.HasOne<TaskFlowUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
        model.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Hash); e.Property(x => x.Hash).HasMaxLength(64);
            e.HasOne<RefreshSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SessionId);
        });
        model.Entity<Workspace>(e => { e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(120); });
        model.Entity<Membership>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, UserId = x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProjectShare>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.ProjectId, x.UserId });
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskItem>(e =>
        {
            e.ToTable("Tasks", t =>
            {
                t.HasCheckConstraint("CK_Task_Container", "(\"ProjectId\" IS NULL) <> (\"InboxOwnerId\" IS NULL)");
                t.HasCheckConstraint("CK_Task_Due", "(\"DueKind\" = 0 AND \"DueDate\" IS NULL AND \"DueLocalTime\" IS NULL AND \"DueZone\" IS NULL AND \"DueAtUtc\" IS NULL) OR (\"DueKind\" = 1 AND \"DueDate\" IS NOT NULL AND \"DueLocalTime\" IS NULL AND \"DueZone\" IS NULL AND \"DueAtUtc\" IS NULL) OR (\"DueKind\" = 2 AND \"DueDate\" IS NOT NULL AND \"DueLocalTime\" IS NOT NULL AND \"DueZone\" IS NOT NULL AND \"DueAtUtc\" IS NOT NULL)");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.Property(x => x.Title).HasMaxLength(500); e.Property(x => x.Description).HasMaxLength(20000);
            e.Property(x => x.DueZone).HasMaxLength(100); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.InboxOwnerId, x.Id });
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId }).HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, UserId = x.CreatorId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, UserId = x.InboxOwnerId }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
