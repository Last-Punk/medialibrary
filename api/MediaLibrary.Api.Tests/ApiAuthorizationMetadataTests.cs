using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediaLibrary.Api.Documentation;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Security;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;

namespace MediaLibrary.Api.Tests;

public sealed class ApiAuthorizationMetadataTests
{
    [Fact]
    public void LibraryRoutesRequireBearer_BotLoginUsesApiKeyAndAuthRoutesAreAnonymous()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(
                "telegram-auth",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1)
                });
            options.AddFixedWindowLimiter(
                "bot-auth",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 2,
                    Window = TimeSpan.FromMinutes(1)
                });
        });
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"ApiAuthorizationMetadataTests-{Guid.NewGuid()}"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICurrentUser>(TestCurrentUser.Default);
        builder.Services.AddScoped<MediaService>();
        builder.Services.AddScoped<SearchAggregatorService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddHttpClient<TmdbService>();
        builder.Services.AddHttpClient<RawgService>();

        using var app = builder.Build();
        app.MapAuthEndpoints();
        app.MapMediaEndpoints();
        app.MapSearchEndpoints();
        app.MapStatsEndpoints();
        app.MapGenreEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var dataEndpoints = endpoints.Where(endpoint =>
            endpoint.RoutePattern.RawText?.StartsWith("/api/media", StringComparison.Ordinal) == true
            || endpoint.RoutePattern.RawText?.StartsWith("/api/search", StringComparison.Ordinal) == true
            || endpoint.RoutePattern.RawText?.StartsWith("/api/stats", StringComparison.Ordinal) == true
            || endpoint.RoutePattern.RawText?.StartsWith("/api/genres", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(dataEndpoints);
        Assert.All(dataEndpoints, endpoint =>
            Assert.Contains(endpoint.Metadata, metadata => metadata is IAuthorizeData));

        var login = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.TrimEnd('/') == "/api/auth/telegram");
        Assert.Contains(login.Metadata, metadata => metadata is IAllowAnonymous);
        Assert.DoesNotContain(login.Metadata, metadata => metadata is IAuthorizeData);

        var botLogin = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.TrimEnd('/') == "/api/auth/bot");
        Assert.Contains(botLogin.Metadata, metadata => metadata is IAllowAnonymous);
        Assert.DoesNotContain(botLogin.Metadata, metadata => metadata is IAuthorizeData);
        Assert.Contains(botLogin.Metadata, metadata => metadata is BotApiKeyAuthenticationMetadata);
        var botAuthRateLimit = Assert.Single(botLogin.Metadata.OfType<EnableRateLimitingAttribute>());
        Assert.Equal("bot-auth", botAuthRateLimit.PolicyName);
    }
}
