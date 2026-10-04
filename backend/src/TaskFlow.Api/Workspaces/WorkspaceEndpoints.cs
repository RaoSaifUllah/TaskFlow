using TaskFlow.Api.Identity;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Api.Workspaces;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaces(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/workspaces").RequireAuthorization();
        group.MapGet("/", async (IWorkspaceReadService reads, HttpContext context, CancellationToken ct) =>
            Results.Ok(await reads.ListAsync(AuthEndpoints.UserId(context.User), ct)));
        group.MapPatch("/{workspaceId:guid}",async (Guid workspaceId,RenameWorkspace request,IWorkspaceService service,HttpContext context,CancellationToken ct)=>
            await Handle(async ()=> {await service.RenameAsync(Actor(context),workspaceId,request.Name,request.Version,ct);return Results.NoContent();}));
        group.MapDelete("/{workspaceId:guid}",async (Guid workspaceId,long? version,IWorkspaceService service,HttpContext context,CancellationToken ct)=>
            await Handle(async ()=> {await service.DeleteAsync(Actor(context),workspaceId,version,ct);return Results.NoContent();}));
        group.MapPost("/", async (CreateWorkspace request, IWorkspaceService service, HttpContext context, CancellationToken ct) =>
            await Handle(async () => { var id = await service.CreateAsync(Actor(context), request.Name, ct); return Results.Created($"/api/v1/workspaces/{id}", new { id }); }));
        group.MapGet("/{workspaceId:guid}/members", async (Guid workspaceId, IWorkspaceService service, IWorkspaceReadService reads, HttpContext context, CancellationToken ct) =>
            await Handle(async () => { await service.AuthorizeReadAsync(Actor(context), workspaceId, true, ct); return Results.Ok(await reads.MembersAsync(workspaceId, AuthEndpoints.UserId(context.User), ct)); }));
        group.MapPatch("/{workspaceId:guid}/members/{userId:guid}", async (Guid workspaceId, Guid userId, ChangeMember request, IWorkspaceService service, HttpContext context, CancellationToken ct) =>
            await Handle(async () => { await service.ChangeMemberAsync(Actor(context), workspaceId, userId, request.Role, request.Active, ct); return Results.NoContent(); }));
        group.MapPost("/{workspaceId:guid}/invites", async (Guid workspaceId, CreateInvite request, IInviteService service, HttpContext context, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.CreateAsync(Actor(context), workspaceId, request.Email, request.Role, ct)))).RequireRateLimiting("auth");
        app.MapPost("/api/v1/invites/accept", async (AcceptInvite request, IInviteService service, HttpContext context, CancellationToken ct) =>
            await service.AcceptAsync(Actor(context), request.Token, ct) ? Results.NoContent() : AuthEndpoints.Error(400, "invitation_rejected"))
            .RequireAuthorization().RequireRateLimiting("auth");
    }
    private static RequestActor Actor(HttpContext context) => new(AuthEndpoints.UserId(context.User), AuthEndpoints.SessionId(context.User));
    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); } catch (WorkspaceRejectedException error) { return AuthEndpoints.Error(error.Status, error.Code); }
    }
    public sealed record CreateWorkspace(string Name);
    public sealed record RenameWorkspace(string Name,long? Version);
    public sealed record ChangeMember(WorkspaceRole Role, bool Active);
    public sealed record CreateInvite(string Email, WorkspaceRole Role);
    public sealed record AcceptInvite(string Token);
}
