using PalOps.Web.Contracts;

namespace PalOps.Web.AdvancedOperations;

public sealed class IntegrationApiTokenMiddleware(RequestDelegate next)
{
    public const string ValidationItemKey = "PalOps.IntegrationToken";

    public async Task InvokeAsync(HttpContext context, ISecurityCenterService security)
    {
        var isIntegrationApi = context.Request.Path.StartsWithSegments("/api/v1/integrations");
        var requiredScope = ResolveRequiredScope(context.Request);
        if (requiredScope is null)
        {
            if (isIntegrationApi)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : string.Empty;
        var validation = await security.ValidateTokenAsync(
            token,
            requiredScope,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.RequestAborted);
        if (!validation.Valid)
        {
            context.Response.StatusCode = string.Equals(validation.FailureReason, "Bearer token scope is insufficient.", StringComparison.Ordinal)
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(
                new ApiErrorEnvelope(
                    new ApiError(
                        context.Response.StatusCode == StatusCodes.Status403Forbidden ? "TOKEN_SCOPE_INSUFFICIENT" : "TOKEN_INVALID",
                        context.Response.StatusCode == StatusCodes.Status403Forbidden ? "API token scope is insufficient." : "A valid API bearer token is required.",
                        null,
                        null,
                        null),
                    context.TraceIdentifier),
                context.RequestAborted);
            return;
        }

        context.Items[ValidationItemKey] = validation;
        await next(context);
    }

    private static string? ResolveRequiredScope(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api/v1/integrations", out var remaining)) return null;
        if (HttpMethods.IsGet(request.Method) && string.Equals(remaining.Value, "/status", StringComparison.OrdinalIgnoreCase)) return "status.read";
        if (HttpMethods.IsGet(request.Method) && string.Equals(remaining.Value, "/incidents", StringComparison.OrdinalIgnoreCase)) return "incidents.read";
        if (HttpMethods.IsGet(request.Method) && string.Equals(remaining.Value, "/diagnostics", StringComparison.OrdinalIgnoreCase)) return "diagnostics.read";
        if (HttpMethods.IsPost(request.Method) && string.Equals(remaining.Value, "/events", StringComparison.OrdinalIgnoreCase)) return "events.write";
        return null;
    }
}
