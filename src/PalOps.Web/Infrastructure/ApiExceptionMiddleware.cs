using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using PalOps.Web.Contracts;
using PalOps.Web.External;
using PalOps.Web.Grants;
using PalOps.Web.Rcon;
using PalOps.Web.SaveGames;

namespace PalOps.Web.Infrastructure;

public sealed class ForbiddenOperationException(string message) : Exception(message);

public class PalOpsApiException(
    int statusCode,
    string code,
    string message,
    string? detail = null,
    string? suggestedAction = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
    public string? SuggestedAction { get; } = suggestedAction;
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (context.Request.Path.StartsWithSegments("/api"))
        {
            if (context.Response.HasStarted) throw;
            var (status, code, message, details, detail, suggestedAction) = Map(exception, context.TraceIdentifier);
            if (status >= 500) logger.LogError(exception, "API request failed with {Code}. TraceId: {TraceId}", code, context.TraceIdentifier);
            else logger.LogWarning("API request rejected with {Code}: {Message}", code, message);
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiError(code, message, details, detail, suggestedAction), context.TraceIdentifier), context.RequestAborted);
        }
    }

    private static (int Status, string Code, string Message, object? Details, string? Detail, string? SuggestedAction) Map(Exception exception, string traceId)
        => exception switch
        {
            PalOpsApiException ex => (ex.StatusCode, ex.Code, ex.Message, null, ex.Detail, ex.SuggestedAction),
            GrantValidationException ex => (StatusCodes.Status400BadRequest, ex.Code, ex.Message, null, null, null),
            AntiforgeryValidationException => (StatusCodes.Status400BadRequest, "CSRF_VALIDATION_FAILED", "安全校验失败，请刷新页面后重试。", null, null, null),
            KeyNotFoundException ex => (StatusCodes.Status404NotFound, "NOT_FOUND", ex.Message, null, null, null),
            FileNotFoundException ex => (StatusCodes.Status404NotFound, "FILE_NOT_FOUND", ex.Message, new { traceId }, null, null),
            DirectoryNotFoundException ex => (StatusCodes.Status422UnprocessableEntity, "DIRECTORY_NOT_FOUND", ex.Message, new { traceId }, null, null),
            InvalidDataException ex => (StatusCodes.Status422UnprocessableEntity, "INVALID_DATA", ex.Message, new { traceId }, null, null),
            InvalidOperationException ex => (StatusCodes.Status409Conflict, "STATE_CONFLICT", ex.Message, new { traceId }, null, null),
            SaveIndexUnavailableException ex => (StatusCodes.Status503ServiceUnavailable, "SAVE_INDEX_UNAVAILABLE", ex.Message, new { traceId }, null, null),
            ExternalApiException ex => (StatusCodes.Status502BadGateway, ex.Code, ex.Message, ex.Details, null, null),
            RconException ex => (StatusCodes.Status502BadGateway, ex.Code, ex.Message, null, null, null),
            ArgumentException ex => (StatusCodes.Status400BadRequest, "INVALID_REQUEST", ex.Message, null, null, null),
            JsonException => (StatusCodes.Status400BadRequest, "INVALID_JSON", "请求 JSON 格式无效。", null, null, null),
            ForbiddenOperationException ex => (StatusCodes.Status403Forbidden, "FORBIDDEN", ex.Message, null, null, null),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "身份验证失败。", null, null, null),
            OperationCanceledException => (StatusCodes.Status408RequestTimeout, "REQUEST_CANCELLED", "请求已取消或超时。", null, null, null),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "服务器内部错误。", new { traceId }, null, null)
        };
}
