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
    [Fact]
    public async Task AuthenticateTelegram_InvalidInitDataReturns401WithStableErrorCode()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AuthEndpointsTests-{Guid.NewGuid()}")
            .Options);
        var auth = new AuthService(
            db,
            Options.Create(new TelegramOptions { BotToken = "test-bot-token", InitDataMaxAgeMinutes = 30 }),
            Options.Create(new JwtOptions
            {
                Issuer = "test-issuer",
                Audience = "test-audience",
                Secret = "test-only-signing-secret-with-more-than-32-bytes",
                ExpiresHours = 1
            }),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthService>.Instance);
        var handler = typeof(AuthEndpoints).GetMethod(
            "AuthenticateTelegram",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("AuthEndpoints.AuthenticateTelegram not found.");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[] { new TelegramAuthRequest { InitData = "tampered" }, auth, CancellationToken.None })!;

        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = stream;
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("INVALID_INIT_DATA", document.RootElement.GetProperty("code").GetString());
        Assert.Empty(await db.Users.ToListAsync());
    }
}
