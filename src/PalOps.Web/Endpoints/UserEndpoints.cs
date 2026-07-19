using System.Security.Claims;
using PalOps.Web.Audit;
using PalOps.Web.Contracts;
using PalOps.Web.Security;

namespace PalOps.Web.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/users").RequireAuthorization("OwnerOnly").WithTags("Users");
        group.MapGet("", async (IUserAccountStore store, HttpContext context, CancellationToken cancellationToken) =>
            Results.Ok(new ApiResponse<IReadOnlyList<UserAccountResponse>>(
                (await store.ListAsync(cancellationToken)).Select(ToResponse).ToArray(), context.TraceIdentifier, [])));

        group.MapPost("", async (CreateUserRequest request, IUserAccountStore store, IPasswordHasher hasher, IAuditLogService audit, HttpContext context, CancellationToken cancellationToken) =>
        {
            var account = await store.CreateAsync(request.UserName, request.DisplayName, hasher.Hash(request.Password), request.Role, request.Enabled, cancellationToken);
            await audit.WriteAsync("users.create", "success", account.Id, $"创建用户 {account.UserName}。", new { account.Role, account.Enabled }, cancellationToken);
            return Results.Ok(new ApiResponse<UserAccountResponse>(ToResponse(account), context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPut("/{id}", async (string id, UpdateUserRequest request, IUserAccountStore store, IAuditLogService audit, HttpContext context, CancellationToken cancellationToken) =>
        {
            var account = await store.UpdateAsync(id, request.DisplayName, request.Role, request.Enabled, cancellationToken);
            await audit.WriteAsync("users.update", "success", account.Id, $"更新用户 {account.UserName}。", new { account.Role, account.Enabled }, cancellationToken);
            return Results.Ok(new ApiResponse<UserAccountResponse>(ToResponse(account), context.TraceIdentifier, []));
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/{id}/reset-password", async (string id, ResetUserPasswordRequest request, IUserAccountStore store, IPasswordHasher hasher, IAuditLogService audit, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(request.Confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal)) throw new ArgumentException("重置用户密码必须输入 CONFIRM。");
            var account = await store.SetPasswordAsync(id, hasher.Hash(request.Password), cancellationToken);
            await audit.WriteAsync("users.password-reset", "success", account.Id, $"重置用户 {account.UserName} 的密码。", cancellationToken: cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("/{id}", async (string id, string confirmation, IUserAccountStore store, IAuditLogService audit, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(confirmation?.Trim(), "CONFIRM", StringComparison.Ordinal)) throw new ArgumentException("删除用户必须输入 CONFIRM。");
            var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            await store.DeleteAsync(id, currentUserId, cancellationToken);
            await audit.WriteAsync("users.delete", "success", id, "删除用户账户。", cancellationToken: cancellationToken);
            return Results.NoContent();
        }).AddEndpointFilter<CsrfValidationFilter>();
        return endpoints;
    }

    private static UserAccountResponse ToResponse(UserAccount account) => new(account.Id, account.UserName, account.DisplayName, account.Role, account.Enabled, account.CreatedAt, account.UpdatedAt, account.LastLoginAt);
}
