using Microsoft.AspNetCore.RateLimiting;
using MediaLibrary.Api.Documentation;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Endpoints;

/// <summary>Authentication endpoints for Telegram Web App and trusted bot-server clients.</summary>
public static class AuthEndpoints
{
    private const string BotApiKeyHeader = "X-Bot-Api-Key";

    /// <summary>Maps public authentication exchange endpoints.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/telegram", AuthenticateTelegram)
            .AllowAnonymous()
            .RequireRateLimiting("telegram-auth")
            .WithName("AuthenticateTelegram")
            .WithSummary("Authenticate a Telegram Web App user")
            .WithDescription("Validates Telegram Web App initData and returns the user's API JWT.");

        group.MapPost("/bot", AuthenticateBot)
            .AllowAnonymous()
            .RequireRateLimiting("bot-auth")
            .WithMetadata(new BotApiKeyAuthenticationMetadata())
            .WithName("AuthenticateBotServerUser")
            .WithSummary("Authenticate a Telegram user through the trusted bot server")
            .WithDescription("Requires the private X-Bot-Api-Key header and a Telegram user ID from a verified Telegram update.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> AuthenticateTelegram(
        TelegramAuthRequest? request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var result = await authService.AuthenticateTelegramAsync(request?.InitData, cancellationToken);
        if (result is null)
        {
            return Results.Json(
                Error("Invalid Telegram init data", "INVALID_INIT_DATA"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TokenResponse(result);
    }

    private static async Task<IResult> AuthenticateBot(
        BotAuthRequest? request,
        HttpContext context,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        if (!authService.IsBotAuthenticationConfigured)
        {
            return Results.Json(
                Error("Bot authentication is not configured", "BOT_AUTH_UNAVAILABLE"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var apiKey = context.Request.Headers[BotApiKeyHeader].ToString();
        TelegramAuthResult? result;
        try
        {
            result = await authService.AuthenticateBotAsync(
                apiKey,
                request?.TelegramUserId ?? 0,
                cancellationToken);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(Error(ex.Message, "VALIDATION_ERROR"));
        }

        if (result is null)
        {
            return Results.Json(
                Error("Unauthorized", "UNAUTHORIZED"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TokenResponse(result);
    }

    private static IResult TokenResponse(TelegramAuthResult result) => Results.Ok(new
    {
        token = result.Token,
        user = new
        {
            id = result.User.Id,
            telegramId = result.User.TelegramUserId,
            username = result.User.Username,
            firstName = result.User.FirstName
        }
    });

    private static object Error(string message, string code) => new
    {
        error = message,
        code
    };
}
