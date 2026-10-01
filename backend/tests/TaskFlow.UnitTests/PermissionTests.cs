using TaskFlow.Application.Authorization;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.UnitTests;

public sealed class PermissionTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner, true, true)]
    [InlineData(WorkspaceRole.Admin, true, true)]
    [InlineData(WorkspaceRole.Member, false, false)]
    [InlineData(WorkspaceRole.Guest, false, false)]
    public void UnsharedProjectFollowsRolePolicy(WorkspaceRole role, bool read, bool edit)
    {
        var project = new Project { WorkspaceId = Guid.NewGuid(), CreatorId = Guid.NewGuid() };
        var member = new Membership { WorkspaceId = project.WorkspaceId, UserId = Guid.NewGuid(), Role = role };
        Assert.Equal(read, ProjectPolicy.CanRead(member, project, null));
        Assert.Equal(edit, ProjectPolicy.CanEdit(member, project, null));
    }
    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Member)]
    [InlineData(WorkspaceRole.Guest)]
    public void NoRoleCanReadSomeoneElsesInbox(WorkspaceRole role)
    {
        var member = new Membership { WorkspaceId = Guid.NewGuid(), UserId = Guid.NewGuid(), Role = role };
        Assert.False(ProjectPolicy.CanReadInbox(member, member.WorkspaceId, Guid.NewGuid()));
    }
    [Fact]
    public void ShareCannotGrantCrossWorkspaceOrRevokedMembershipAccess()
    {
        var project = new Project { WorkspaceId = Guid.NewGuid() };
        var member = new Membership { WorkspaceId = Guid.NewGuid(), UserId = Guid.NewGuid(), Role = WorkspaceRole.Owner };
        var share = new ProjectShare { WorkspaceId = project.WorkspaceId, ProjectId = project.Id, UserId = member.UserId, Access = ProjectAccess.Editor };
        Assert.False(ProjectPolicy.CanRead(member, project, share));
        member.WorkspaceId = project.WorkspaceId;
        member.Active = false;
        Assert.False(ProjectPolicy.CanRead(member, project, share));
        Assert.False(ProjectPolicy.CanEdit(member, project, share));
    }
    [Fact]
    public void GuestShareAlwaysRemainsReadOnlyEvenWithInvalidEditorGrant()
    {
        var project = new Project { WorkspaceId = Guid.NewGuid() };
        var member = new Membership { WorkspaceId = project.WorkspaceId, UserId = Guid.NewGuid(), Role = WorkspaceRole.Guest };
        var share = new ProjectShare { WorkspaceId = project.WorkspaceId, ProjectId = project.Id, UserId = member.UserId, Access = ProjectAccess.Editor };
        Assert.True(ProjectPolicy.CanRead(member, project, share));
        Assert.False(ProjectPolicy.CanEdit(member, project, share));
    }
    [Fact]
    public void PrivateInboxTaskHasNoFabricatedUtcMidnightAndRejectsStaleEdits()
    {
        var owner = Guid.NewGuid();
        var task = new TaskItem(Guid.NewGuid(), owner, "  A task  ");
        Assert.Null(task.ProjectId);
        Assert.Equal(owner, task.InboxOwnerId);
        task.SetDate(new DateOnly(2026, 10, 1));
        Assert.Null(task.DueAtUtc);
        Assert.Throws<InvalidOperationException>(() => task.Rename("Stale", 1));
        Assert.Equal("A task", task.Title);
    }
}
