using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using TaskFlow.Application.Identity;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Identity;

public static class AuthEndpoints
{
    public const string RefreshCookie = "__Secure-TaskFlow.Refresh";
    public const string CsrfCookie = "__Host-TaskFlow.Csrf";
    public static void MapAuth(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");
        group.MapGet("/csrf", (HttpContext context) =>
        {
            var csrf = context.Request.Cookies[CsrfCookie];
            if (csrf is null || csrf.Length is < 32 or > 128) csrf = AuthService.NewOpaqueToken();
            context.Response.Cookies.Append(CsrfCookie, csrf, Cookie(false, "/", DateTimeOffset.UtcNow.AddDays(30)));
            return Results.Ok(new { csrfToken = csrf });
        });
        group.MapPost("/bootstrap", async (BootstrapRequest request, IAccountService accounts, HttpContext context, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!CookieMutationAllowed(context, configuration)) return Error(403, "csrf_rejected");
            if (request.Email is null || request.Password is null || request.Token is null || request.WorkspaceName is null || request.TimeZone is null)
                return Error(400, "invalid_input");
            return await accounts.BootstrapAsync(request, ct) ? Results.NoContent() : Error(400, "bootstrap_rejected");
        }).RequireRateLimiting("auth");
        group.MapPost("/login", async (LoginRequest request, IAuthService auth, HttpContext context, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!CookieMutationAllowed(context, configuration)) return Error(403, "csrf_rejected");
            if (request.Email is null || request.Password is null || request.Device is null) return Error(400, "invalid_input");
            var grant = await auth.LoginAsync(request.Email, request.Password, request.Code, request.Device, ct);
            return grant is null ? Error(401, "login_rejected") : Grant(context, grant);
        }).RequireRateLimiting("auth");
        group.MapPost("/refresh", async (IAuthService auth, HttpContext context, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!CookieMutationAllowed(context, configuration)) return Error(403, "csrf_rejected");
            var refresh = context.Request.Cookies[RefreshCookie];
            if (refresh is null) return Error(401, "session_required");
            var grant = await auth.RotateAsync(refresh, context.Request.Headers["X-CSRF-Token"].ToString(), ct);
            if (grant is not null) return Grant(context, grant);
            ClearCookies(context);
            return Error(401, "session_expired");
        }).RequireRateLimiting("auth");
        group.MapPost("/logout", async (IAuthService auth, HttpContext context, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!CookieMutationAllowed(context, configuration)) return Error(403, "csrf_rejected");
            await auth.RevokeAsync(UserId(context.User), SessionId(context.User), ct);
            ClearCookies(context);
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapGet("/sessions", async (IAuthService auth, HttpContext context, CancellationToken ct) =>
            Results.Ok(await auth.SessionsAsync(UserId(context.User), SessionId(context.User), ct))).RequireAuthorization();
        group.MapDelete("/sessions/{id:guid}", async (Guid id, IAuthService auth, HttpContext context, CancellationToken ct) =>
        {
            await auth.RevokeAsync(UserId(context.User), id, ct);
            if (id == SessionId(context.User)) ClearCookies(context);
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapPost("/mfa/begin", async (PasswordRequest request, IAccountService accounts, HttpContext context, CancellationToken ct) =>
        {
            if (request.Password is null) return Error(400, "invalid_input");
            var key = await accounts.BeginMfaAsync(UserId(context.User), SessionId(context.User), request.Password, ct);
            return key is null ? Error(400, "mfa_setup_rejected") : Results.Ok(new { secret = key, issuer = "TaskFlow" });
        }).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/mfa/confirm", async (CodeRequest request, IAccountService accounts, HttpContext context, CancellationToken ct) =>
        {
            if (request.Code is null) return Error(400, "invalid_input");
            var codes = await accounts.ConfirmMfaAsync(UserId(context.User), request.Code, ct);
            if (codes is null) return Error(400, "mfa_code_rejected");
            ClearCookies(context);
            return Results.Ok(new { recoveryCodes = codes, requiresLogin = true });
        }).RequireAuthorization().RequireRateLimiting("auth");
        group.MapPost("/step-up", async (CodeRequest request, IAccountService accounts, HttpContext context, CancellationToken ct) =>
            request.Code is not null && await accounts.StepUpAsync(UserId(context.User), SessionId(context.User), request.Code, ct)
                ? Results.NoContent() : Error(400, "mfa_code_rejected")).RequireAuthorization().RequireRateLimiting("auth");
    }
    public static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sub")!);
    public static Guid SessionId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("sid")!);
    public static bool CookieMutationAllowed(HttpContext context, IConfiguration configuration)
    {
        if (context.Request.Headers.Origin.ToString() != configuration["Auth:Origin"]) return false;
        var header = context.Request.Headers["X-CSRF-Token"].ToString();
        var cookie = context.Request.Cookies[CsrfCookie];
        return cookie is not null && header.Length is >= 32 and <= 128 && cookie.Length is >= 32 and <= 128 &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(header)), SHA256.HashData(Encoding.UTF8.GetBytes(cookie)));
    }
    private static CookieOptions Cookie(bool httpOnly, string path, DateTimeOffset expires) => new()
    { HttpOnly = httpOnly, Secure = true, SameSite = SameSiteMode.Strict, Path = path, Expires = expires, IsEssential = true };
    private static IResult Grant(HttpContext context, AuthGrant grant)
    {
        context.Response.Cookies.Append(RefreshCookie, grant.RefreshToken, Cookie(true, "/api/v1/auth", DateTimeOffset.UtcNow.AddDays(7)));
        context.Response.Cookies.Append(CsrfCookie, grant.CsrfToken, Cookie(false, "/", DateTimeOffset.UtcNow.AddDays(30)));
        return Results.Ok(new { grant.AccessToken, grant.ExpiresAt, grant.User, grant.CsrfToken });
    }
    private static void ClearCookies(HttpContext context)
    {
        context.Response.Cookies.Delete(RefreshCookie, Cookie(true, "/api/v1/auth", DateTimeOffset.UtcNow));
        context.Response.Cookies.Delete(CsrfCookie, Cookie(false, "/", DateTimeOffset.UtcNow));
    }
    public static IResult Error(int status, string code) => Results.Problem(statusCode: status, title: code.Replace('_', ' '), extensions: new Dictionary<string, object?> { ["code"] = code });
    public sealed record LoginRequest(string Email, string Password, string? Code, string Device);
    public sealed record PasswordRequest(string Password);
    public sealed record CodeRequest(string Code);
}
