using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// GET /api/genres returns a dictionary sorted by name using the { genres: [...] } response shape.
/// The route matches the contract path /api/genres (the trailing slash in RawText is an artifact
/// of combining MapGroup and MapGet(""), and does not affect routing).
/// </summary>
public class GenreEndpointsTests
{
    [Fact]
    public async Task GetAll_ReturnsGenresSortedByName()
    {
        using var db = CreateDb();
        var genres = new[]
        {
            new Genre { Name = "Thriller" },
            new Genre { Name = "Action" },
            new Genre { Name = "Sci-Fi" }
        };
        SeedGenreMedia(db, genres);

        var json = await ExecuteGetAllAsync(db);

        var names = json.GetProperty("genres").EnumerateArray()
            .Select(g => g.GetProperty("name").GetString())
            .ToList();

        Assert.Equal(new[] { "Action", "Sci-Fi", "Thriller" }, names);
    }

    [Fact]
    public async Task GetAll_ReturnsContractShape_WithIdAndNameOnly()
    {
        using var db = CreateDb();
        var genres = new[] { new Genre { Name = "Action" } };
        SeedGenreMedia(db, genres);
        var genreId = await db.Genres.Select(g => g.Id).SingleAsync();

        var json = await ExecuteGetAllAsync(db);

        var genre = Assert.Single(json.GetProperty("genres").EnumerateArray());
        Assert.Equal(new[] { "id", "name" }, genre.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(genreId, genre.GetProperty("id").GetInt32());
        Assert.Equal("Action", genre.GetProperty("name").GetString());
    }

    [Fact]
    public void MapGenreEndpoints_MapsExactlyTheContractRoute()
    {
        var builder = WebApplication.CreateBuilder();
        // AppDbContext must be registered in DI; otherwise the handler parameter is treated
        // as FromBody and route construction fails before the path can be tested.
        builder.Services.AddDbContext<AppDbContext>(opt =>
            opt.UseInMemoryDatabase($"GenreRouteTests-{Guid.NewGuid()}"));
        builder.Services.AddSingleton<ICurrentUser>(TestCurrentUser.Default);
        builder.Services.AddAuthorization();
        using var app = builder.Build();

        app.MapGenreEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        // Combining MapGroup("/api/genres") and MapGet("") produces this RawText value:
        // "/api/genres/". This is a cosmetic route-pattern artifact; routing ignores the
        // trailing slash. The contract path is /api/genres, which is also the path seen by
        // Swagger (see the exported swagger.json) and API clients.
        var route = Assert.Single(endpoints, e =>
            (e.RoutePattern.RawText ?? string.Empty).TrimEnd('/') == "/api/genres");
        var methods = route.Metadata.OfType<HttpMethodMetadata>().SelectMany(m => m.HttpMethods);
        Assert.Contains("GET", methods);
        // No other routes under /api/genres* should be registered.
        Assert.Single(endpoints, e =>
            (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/genres", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAll_HidesGenresUsedOnlyByOtherOwners()
    {
        using var db = CreateDb();
        SeedGenreMedia(db, new[] { new Genre { Name = "Personal Drama" } }, ownerId: 101);
        SeedGenreMedia(db, new[] { new Genre { Name = "Private Horror" } }, ownerId: 202);

        var json = await ExecuteGetAllAsync(db, new TestCurrentUser(101));

        var genres = json.GetProperty("genres").EnumerateArray()
            .Select(genre => genre.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(new[] { "Personal Drama" }, genres);
    }

    private static async Task<JsonElement> ExecuteGetAllAsync(
        AppDbContext db,
        TestCurrentUser? currentUser = null)
    {
        var handler = typeof(GenreEndpoints).GetMethod("GetAll", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GenreEndpoints.GetAll not found.");

        var result = await (Task<IResult>)handler.Invoke(null, new object[] { db, currentUser ?? TestCurrentUser.Default })!;

        await using var stream = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = stream;

        await result.ExecuteAsync(context);

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }


    private static void SeedGenreMedia(AppDbContext db, IEnumerable<Genre> genres, int ownerId = 1)
    {
        db.MediaItems.Add(new MediaItem
        {
            OwnerId = ownerId,
            Title = "Fixture",
            OriginalTitle = "Fixture",
            Type = MediaType.Movie,
            Status = MediaStatus.Plan,
            CreatedAt = DateTime.UtcNow,
            Genres = genres.ToList()
        });
        db.SaveChanges();
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"GenreEndpointsTests-{Guid.NewGuid()}")
            .Options);
}
