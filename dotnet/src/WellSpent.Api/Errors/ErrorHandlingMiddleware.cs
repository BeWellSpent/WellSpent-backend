using System.Text.Json;

namespace WellSpent.Api.Errors;

public sealed record ErrorBody(string Code, string Message);

/// <summary>
/// Global exception -> HTTP mapping. Mirrors internal/rest/errors.go's
/// statusForError one-for-one, including the lowercase error codes: the same
/// error kind must mean the same HTTP status/code whichever backend answers a
/// given request during the strangler-fig cutover (see
/// docs/features/refactor-backend-csharp.md).
/// </summary>
public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (status, code) = MapException(ex);

            // Logged with the resolved code so a 500 is traceable back to the
            // originating exception type without grepping a raw stack trace.
            logger.LogError(ex, "Unhandled exception mapped to {ErrorCode} ({Status})", code, status);

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new ErrorBody(code, ex.Message)));
        }
    }

    private static (int Status, string Code) MapException(Exception ex) => ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "not_found"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "forbidden"),
        DuplicateException => (StatusCodes.Status409Conflict, "already_exists"),
        AppValidationException => (StatusCodes.Status400BadRequest, "invalid_argument"),
        _ => (StatusCodes.Status500InternalServerError, "internal"),
    };
}
