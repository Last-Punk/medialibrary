using Microsoft.AspNetCore.RateLimiting;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Endpoints;

/// <summary>Authentication endpoints for Telegram Web App clients.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps public authentication endpoints.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/telegram", AuthenticateTelegram)
            .AllowAnonymous()
            .RequireRateLimiting("telegram-auth")
            .WithName("AuthenticateTelegram");

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
                new { error = "Invalid Telegram init data", code = "INVALID_INIT_DATA" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(new
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
    }
}
