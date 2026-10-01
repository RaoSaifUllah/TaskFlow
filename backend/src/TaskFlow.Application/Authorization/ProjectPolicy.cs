using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Application.Authorization;

public static class ProjectPolicy
{
    public static bool CanRead(Membership? member, Project project, ProjectShare? share) =>
        Valid(member, project) && (member!.Role != WorkspaceRole.Guest &&
        (member.Role is WorkspaceRole.Owner or WorkspaceRole.Admin || project.CreatorId == member.UserId) || ValidShare(member, project, share));

    public static bool CanEdit(Membership? member, Project project, ProjectShare? share) =>
        Valid(member, project) && member!.Role != WorkspaceRole.Guest &&
        (member.Role is WorkspaceRole.Owner or WorkspaceRole.Admin || project.CreatorId == member.UserId ||
        ValidShare(member, project, share) && share!.Access == ProjectAccess.Editor);

    public static bool CanReadInbox(Membership? member, Guid workspaceId, Guid ownerId) =>
        member is { Active: true } && member.Role != WorkspaceRole.Guest && member.WorkspaceId == workspaceId && member.UserId == ownerId;

    private static bool Valid(Membership? member, Project project) => member is { Active: true } && member.WorkspaceId == project.WorkspaceId;
    private static bool ValidShare(Membership? member, Project project, ProjectShare? share) => share is not null && member is not null &&
        share.WorkspaceId == project.WorkspaceId && share.ProjectId == project.Id && share.UserId == member.UserId;
}
