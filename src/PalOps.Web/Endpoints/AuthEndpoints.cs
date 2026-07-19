using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/status", async (HttpContext context, IUserAccountStore users, CancellationToken cancellationToken) =>
        {
            var configured = await users.IsConfiguredAsync(cancellationToken);
            var authenticated = context.User.Identity?.IsAuthenticated == true;
            return Results.Ok(new AuthStatusResponse(
                authenticated,
                !configured,
                authenticated ? context.User.Identity?.Name : null,
                authenticated ? context.User.FindFirstValue(ClaimTypes.Role) : null,
                authenticated ? context.User.FindFirstValue("display_name") : null));
        });

        group.MapPost("/setup", SetupAsync).RequireRateLimiting("login");
        group.MapPost("/login", LoginAsync).RequireRateLimiting("login");

        group.MapPost("/logout", async (HttpContext context, IAuditLogService audit, CancellationToken cancellationToken) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await audit.WriteAsync("auth.logout", "success", EndpointHelpers.RemoteIp(context), "用户退出登录。", cancellationToken: cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            HttpContext context,
            IUserAccountStore users,
            IAuthStateStore legacyStore,
            IPasswordHasher hasher,
            IAuditLogService audit,
            CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
            var account = await users.FindByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedAccessException();
            if (!hasher.Verify(request.CurrentPassword, account.PasswordHash))
            {
                await audit.WriteAsync("auth.password-change", "denied", EndpointHelpers.RemoteIp(context), "旧密码校验失败。", new { account.UserName }, cancellationToken);
                throw new UnauthorizedAccessException();
            }

            var passwordHash = hasher.Hash(request.NewPassword);
            await users.SetPasswordAsync(account.Id, passwordHash, cancellationToken);
            if (account.Role == PalOpsRoles.Owner) await legacyStore.SetPasswordAsync(request.NewPassword, cancellationToken);
            await audit.WriteAsync("auth.password-change", "success", EndpointHelpers.RemoteIp(context), "用户密码已修改。", new { account.UserName }, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfValidationFilter>();

        group.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfResponse(tokens.RequestToken ?? throw new InvalidOperationException("无法生成 CSRF Token。")));
        }).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> SetupAsync(
        LoginRequest request,
        HttpContext context,
        IAuthStateStore legacyStore,
        IUserAccountStore users,
        IAuditLogService audit,
        CancellationToken cancellationToken)
    {
        if (await users.IsConfiguredAsync(cancellationToken))
            return Results.Conflict(new ApiErrorEnvelope(new ApiError("ALREADY_CONFIGURED", "管理员账户已经设置。"), context.TraceIdentifier));

        await legacyStore.SetPasswordAsync(request.Password, cancellationToken);
        var legacy = await legacyStore.GetAsync(cancellationToken) ?? throw new InvalidOperationException("无法保存管理员密码。");
        var owner = await users.CreateOwnerAsync(legacy.PasswordHash, cancellationToken);
        await SignInAsync(context, owner);
        await audit.WriteAsync("auth.setup", "success", EndpointHelpers.RemoteIp(context), "完成首次所有者账户设置。", cancellationToken: cancellationToken);
        return Results.Ok(ToStatus(owner));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IUserAccountStore users,
        IPasswordHasher hasher,
        ILoginAttemptTracker attempts,
        IAuditLogService audit,
        CancellationToken cancellationToken)
    {
        var userName = string.IsNullOrWhiteSpace(request.UserName) ? "admin" : request.UserName.Trim();
        var remoteIp = EndpointHelpers.RemoteIp(context);
        var key = $"{remoteIp}|{userName.ToLowerInvariant()}";
        var attemptState = attempts.GetState(key);
        if (attemptState.IsLocked)
        {
            context.Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(attemptState.RetryAfter.TotalSeconds)).ToString();
            await audit.WriteAsync("auth.login", "locked", remoteIp, "登录因连续失败被临时锁定。", new { userName, retryAfterSeconds = (int)attemptState.RetryAfter.TotalSeconds }, cancellationToken);
            return Results.Json(new ApiErrorEnvelope(new ApiError("LOGIN_LOCKED", "登录失败次数过多，请稍后重试。"), context.TraceIdentifier), statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!await users.IsConfiguredAsync(cancellationToken))
            return Results.Conflict(new ApiErrorEnvelope(new ApiError("SETUP_REQUIRED", "需要先设置管理员账户。"), context.TraceIdentifier));

        var account = await users.FindByUserNameAsync(userName, cancellationToken);
        if (account is null || !account.Enabled || !hasher.Verify(request.Password, account.PasswordHash))
        {
            attempts.RecordFailure(key);
            await audit.WriteAsync("auth.login", "denied", remoteIp, "用户登录失败。", new { userName }, cancellationToken);
            throw new UnauthorizedAccessException();
        }

        attempts.RecordSuccess(key);
        await users.RecordLoginAsync(account.Id, cancellationToken);
        await SignInAsync(context, account);
        await audit.WriteAsync("auth.login", "success", remoteIp, "用户登录成功。", new { account.UserName, account.Role }, cancellationToken);
        return Results.Ok(ToStatus(account));
    }

    private static AuthStatusResponse ToStatus(UserAccount account)
        => new(true, false, account.UserName, account.Role, account.DisplayName);

    private static Task SignInAsync(HttpContext context, UserAccount account)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, account.Id),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(ClaimTypes.Role, account.Role),
            new Claim("display_name", account.DisplayName)
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }
}
