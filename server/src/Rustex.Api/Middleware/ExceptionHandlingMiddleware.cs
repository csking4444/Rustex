using System.Text.Json;

namespace Rustex.Api.Middleware;

/// <summary>Turns exceptions into consistent JSON without leaking internals.
///
/// The split that matters: a handful of exception types carry a message we deliberately wrote for
/// the end user and are safe to return verbatim. Everything else is a bug or an infrastructure
/// fault, and its message could contain a connection string, a file path or a provider key — so
/// it is logged in full and replaced with a generic sentence on the way out.</summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Too late to change the status code — the body is already going out. Log and let
                // the connection fail rather than writing a second, malformed response.
                _log.LogError(ex, "Exception after response started for {Path}", context.Request.Path);
                throw;
            }

            var (status, error, message) = Classify(ex);

            if (status >= StatusCodes.Status500InternalServerError)
            {
                _log.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                // The cause goes to the log even for 4xx. The client still gets only the generic
                // message, but without this a mapped 400 is undiagnosable from the outside — which
                // is exactly the position a "bad_request" with no further detail leaves you in.
                _log.LogInformation(ex, "Rejected {Method} {Path}: {Error}", context.Request.Method, context.Request.Path, error);
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error,
                message,
                // Lets a user quote something actionable in a support request without us having to
                // expose the exception itself.
                traceId = context.TraceIdentifier,
            }));
        }
    }

    private static (int Status, string Error, string Message) Classify(Exception ex) => ex switch
    {
        // Our own argument guards, e.g. an unparseable id that got past model validation.
        ArgumentException or FormatException => (StatusCodes.Status400BadRequest, "bad_request", "The request was not valid."),

        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "forbidden", "You do not have access to that."),

        // Client hung up. Not an error worth a 500 or a stack trace. 499 is nginx's non-standard
        // "client closed request" — ASP.NET Core has no constant for it.
        OperationCanceledException => (499, "cancelled", "The request was cancelled."),

        _ => (StatusCodes.Status500InternalServerError, "server_error", "Something went wrong on our end."),
    };
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseApiExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
