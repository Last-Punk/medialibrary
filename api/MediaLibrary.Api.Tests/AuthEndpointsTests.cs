using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MediaLibrary.Api.Configuration;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Tests;

public sealed class AuthEndpointsTests
{
    private const string BotApiKey = "unit-test-bot-api-key-with-at-least-32-bytes";

    [Fact]
    public async Task AuthenticateTelegram_InvalidInitDataReturns401WithStableErrorCode()
    {
        using var db = CreateDb();
        var auth = CreateAuthService(db);
        var handler = GetHandler("AuthenticateTelegram");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[] { new TelegramAuthRequest { InitData = "tampered" }, auth, CancellationToken.None })!;
        var response = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("INVALID_INIT_DATA", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task AuthenticateBot_InvalidServerCredentialReturns401WithoutCreatingUser()
    {
        using var db = CreateDb();
        var auth = CreateAuthService(db);
        var context = CreateContext();
        context.Request.Headers["X-Bot-Api-Key"] = "wrong-server-key";
        var handler = GetHandler("AuthenticateBot");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[]
            {
                new BotAuthRequest { TelegramUserId = 73421 },
                context,
                auth,
                CancellationToken.None
            })!;
        var response = await ExecuteAsync(result, context);

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("UNAUTHORIZED", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task AuthenticateBot_ValidServerCredentialReturnsUserJwt()
    {
        using var db = CreateDb();
        var auth = CreateAuthService(db);
        var context = CreateContext();
        context.Request.Headers["X-Bot-Api-Key"] = BotApiKey;
        var handler = GetHandler("AuthenticateBot");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[]
            {
                new BotAuthRequest { TelegramUserId = 73421 },
                context,
                auth,
                CancellationToken.None
            })!;
        var response = await ExecuteAsync(result, context);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("token").GetString()));
        Assert.Equal(73421L, document.RootElement.GetProperty("user").GetProperty("telegramId").GetInt64());
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task AuthenticateBot_UnconfiguredCredentialReturns503()
    {
        using var db = CreateDb();
        var auth = CreateAuthService(db, botApiKey: " ");
        var context = CreateContext();
        var handler = GetHandler("AuthenticateBot");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[]
            {
                new BotAuthRequest { TelegramUserId = 73421 },
                context,
                auth,
                CancellationToken.None
            })!;
        var response = await ExecuteAsync(result, context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("BOT_AUTH_UNAVAILABLE", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(await db.Users.ToListAsync());
    }

    private static AuthService CreateAuthService(AppDbContext db, string botApiKey = BotApiKey) => new(
        db,
        Options.Create(new TelegramOptions { BotToken = "test-bot-token", InitDataMaxAgeMinutes = 30 }),
        Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Secret = "test-only-signing-secret-with-more-than-32-bytes",
            ExpiresHours = 1
        }),
        Options.Create(new BotAuthOptions { ApiKey = botApiKey }),
        TimeProvider.System,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"AuthEndpointsTests-{Guid.NewGuid()}")
        .Options);

    private static MethodInfo GetHandler(string methodName) => typeof(AuthEndpoints).GetMethod(
        methodName,
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"AuthEndpoints.{methodName} not found.");

    private static DefaultHttpContext CreateContext() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
    };

    private static async Task<(int StatusCode, string? ContentType, string Body)> ExecuteAsync(
        IResult result,
        DefaultHttpContext? context = null)
    {
        context ??= CreateContext();
        await using var stream = new MemoryStream();
        context.Response.Body = stream;
        await result.ExecuteAsync(context);
        return (
            context.Response.StatusCode,
            context.Response.ContentType,
            System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
