using Claims.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Claims.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException ex => (ex.StatusCode,
                ex.StatusCode == 413 ? "Payload Too Large" : "Invalid HTTP request"),
            ResourceNotFoundException => (404, "Resource not found"),
            RequestValidationException => (400, "Invalid request"),
            ProcessingConflictException => (409, "Processing conflict"),
            _ => (500, "An unexpected error occurred")
        };

        if (status == 500)
            logger.LogError(exception, "Request failed. Trace ID: {TraceId}", context.TraceIdentifier);

        if (status == 400)
        {
            var validation = ApiProblems.Validation(context, new Dictionary<string, string[]>
            {
                ["request"] = [exception is RequestValidationException
                    ? exception.Message : "Invalid HTTP request."]
            });
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(validation,
                options: (System.Text.Json.JsonSerializerOptions?)null,
                contentType: "application/problem+json", cancellationToken: ct);
            return true;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status switch
            {
                413 => "Request body must not exceed 128 KiB.",
                500 => "Contact support with the trace ID.",
                _ when exception is BadHttpRequestException => "The HTTP request could not be accepted.",
                _ => exception.Message
            },
            Extensions = { ["traceId"] = context.TraceIdentifier }
        };
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem,
            options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}

internal static class ApiProblems
{
    public static ValidationProblemDetails Validation(HttpContext context, IDictionary<string, string[]> errors) => new(errors)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid request",
        Type = "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1",
        Extensions = { ["traceId"] = context.TraceIdentifier }
    };
}
