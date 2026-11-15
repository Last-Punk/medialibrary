using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MediaLibrary.Api.Middleware;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// ErrorHandlingMiddleware: мапіння винятків на контрактні помилки { error, code } —
/// DuplicateException → 409 MEDIA_DUPLICATE, ArgumentException → 400 VALIDATION_ERROR,
/// будь-що інше → 500 INTERNAL_ERROR (із логуванням). Вже розпочату відповідь
/// middleware не перезаписує.
/// </summary>
public class ErrorHandlingMiddlewareTests
{
    [Fact]
    public async Task DuplicateException_ReturnsConflict_WithMediaDuplicateContractBody()
    {
        var (status, body, contentType) = await RunAsync(
            _ => throw new DuplicateException("Media already exists"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("application/json", contentType);

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Media already exists", json.GetProperty("error").GetString());
        Assert.Equal("MEDIA_DUPLICATE", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ArgumentException_ReturnsBadRequest_WithValidationErrorCode()
    {
        var (status, body, _) = await RunAsync(
            _ => throw new ArgumentException("Rating must be between 1 and 10"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Rating must be between 1 and 10", json.GetProperty("error").GetString());
        Assert.Equal("VALIDATION_ERROR", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnexpectedException_ReturnsInternalError_WithoutLeakingDetails()
    {
        var (status, body, _) = await RunAsync(
            _ => throw new InvalidOperationException("database connection lost on shard 7"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);

        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Internal server error", json.GetProperty("error").GetString());
        Assert.Equal("INTERNAL_ERROR", json.GetProperty("code").GetString());
        Assert.DoesNotContain("shard 7", body);
    }

    [Fact]
    public async Task UnexpectedException_IsLoggedAsError()
    {
        var logger = new TestLogger<ErrorHandlingMiddleware>();

        var (status, _, _) = await RunAsync(
            _ => throw new InvalidOperationException("boom"),
            logger);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task WhenResponseHasAlreadyStarted_ErrorDoesNotRewriteIt()
    {
        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext();
        // DefaultHttpContext не має транспорту, тож WriteAsync/StartAsync самі по собі
        // не виставляють HasStarted. Емулюємо вже розпочату відповідь підставною фічою;
        // «вже відправлений» клієнту фрагмент кладемо в потік тіла напряму.
        context.Features.Set<IHttpResponseFeature>(
            new AlwaysStartedResponseFeature { Body = stream });
        stream.Write("partial payload"u8);
        Assert.True(context.Response.HasStarted); // передумова тесту

        var middleware = new ErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        Assert.Equal("partial payload", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ErrorPayload_HasExactlyTheContractFields()
    {
        var (_, body, _) = await RunAsync(
            _ => throw new DuplicateException("dup"));

        var fields = JsonDocument.Parse(body).RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(new[] { "error", "code" }, fields);
    }

    [Fact]
    public async Task WithoutException_ResponsePassesThroughUnchanged()
    {
        var (status, body, _) = await RunAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsync("created");
        });

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.Equal("created", body);
    }

    private static async Task<(int Status, string Body, string ContentType)> RunAsync(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware>? logger = null)
    {
        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = stream;

        var middleware = new ErrorHandlingMiddleware(
            next,
            logger ?? NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body, context.Response.ContentType ?? string.Empty);
    }

    /// <summary>Відповідь, що вже «почалась»: HasStarted завжди true (емуляція транспорту).</summary>
    private sealed class AlwaysStartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    /// <summary>Логер у пам'яті для перевірки, що 500-відповіді журналюються.</summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
