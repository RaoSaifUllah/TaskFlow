using TaskFlow.Api.Workspaces;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Application.Identity;
using TaskFlow.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddTaskFlow(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<JwtIssuer>((options, issuer) =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = issuer.ValidationParameters();
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId) ||
                !Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sessionId))
            { context.Fail("invalid_claims"); return; }
            try
            {
                var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                if (!await auth.ValidateSessionAsync(userId, sessionId, context.HttpContext.RequestAborted)) context.Fail("revoked_session");
            }
            catch (System.Data.Common.DbException) { context.HttpContext.Items["auth_unavailable"] = true; context.Fail("authorization_unavailable"); }
        },
        OnChallenge = context =>
        {
            if (context.HttpContext.Items.ContainsKey("auth_unavailable")) { context.HandleResponse(); context.Response.StatusCode = 503; }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // Bound concurrent memory-hard password work across all client IPs.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/api/v1/auth")
            ? RateLimitPartition.GetConcurrencyLimiter("auth", _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("other"));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) => { context.HttpContext.Response.Headers.RetryAfter = "60"; await AuthEndpoints.Error(429, "rate_limited").ExecuteAsync(context.HttpContext); };
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", "request_failed");
});
builder.Services.AddOpenApi();
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Path.StartsWithSegments("/api/v1/auth"))
    {
        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 8192;
        if (context.Request.ContentLength > 8192) { await AuthEndpoints.Error(413, "request_too_large").ExecuteAsync(context); return; }
    }
    try { await next(context); }
    catch (System.Data.Common.DbException) when (!context.Response.HasStarted)
    {
        await AuthEndpoints.Error(503, "database_unavailable").ExecuteAsync(context);
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "alive" }));
app.MapGet("/ready", async (TaskFlowDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapAuth();
app.MapWorkspaces();
app.Run();
public partial class Program { }

