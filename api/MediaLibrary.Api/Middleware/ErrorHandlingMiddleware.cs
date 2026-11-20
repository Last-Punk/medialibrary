using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Middleware;

/// <summary>
/// Fallback handling for exceptions not handled by endpoint-level try/catch blocks.
/// HTTP binding errors return 400, external catalog failures return 503, and all responses
/// use the { error, code } response shape.
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DuplicateException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, ex.Message, "MEDIA_DUPLICATE");
        }
        catch (ExternalApiUnavailableException ex)
        {
            _logger.LogWarning(
                ex,
                "External catalog {Provider} is unavailable while processing {Method} {Path}",
                ex.Provider,
                context.Request.Method,
                context.Request.Path);
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "TMDB or RAWG is unavailable",
                "EXTERNAL_API_UNAVAILABLE");
        }
        catch (BadHttpRequestException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, "VALIDATION_ERROR");
        }
        catch (ArgumentException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, "VALIDATION_ERROR");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "Internal server error", "INTERNAL_ERROR");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        string code)
    {
        // Once the response has started, its status and body must not be changed.
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            error = message,
            code
        });
    }
}
