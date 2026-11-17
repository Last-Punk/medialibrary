using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Middleware;

/// <summary>
/// Страхувальна сітка для винятків поза наявними try/catch ендпоінтів.
/// Помилки прив'язки HTTP-запиту повертаються як 400; решта помилок зберігають формат { error, code }.
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
        // Після початку відповіді змінювати статус і тіло вже небезпечно.
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
