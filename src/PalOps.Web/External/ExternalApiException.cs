using System.Net;

namespace PalOps.Web.External;

public sealed class ExternalApiException : Exception
{
    public ExternalApiException(string code, string message, HttpStatusCode? statusCode = null, object? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }
    public HttpStatusCode? StatusCode { get; }
    public object? Details { get; }
}
