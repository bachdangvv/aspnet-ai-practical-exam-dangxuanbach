using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartShop.Api.Services;

namespace SmartShop.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            AiException ai => (ai.StatusCode, ai.Code, ai.Message),
            DbException or DbUpdateException => (503, "database_unavailable", "The database is unavailable. Please try again."),
            _ => (500, "internal_error", "An unexpected error occurred.")
        };
        // Do not log upstream response bodies, prompts, customer text or API keys.
        logger.LogWarning("Request failed with {Code}; exception type {ExceptionType}", code, exception.GetType().Name);
        context.Response.StatusCode = status;
        if (exception is AiException { RetryAfter: { } retryAfter })
            context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Extensions = { ["traceId"] = context.TraceIdentifier }
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
