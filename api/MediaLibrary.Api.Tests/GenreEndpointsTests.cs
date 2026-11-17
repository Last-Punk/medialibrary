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
/// GET /api/genres: довідник відсортований за name, формат відповіді { genres: [...] },
/// маршрут відповідає контрактному /api/genres (кінцевий слеш у RawText — артефакт
/// складання MapGroup+MapGet(""), на маршрутизацію не впливає).
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
        // AppDbContext має бути в DI, інакше під час побудови маршруту параметр обробника
        // сприйметься як FromBody — і мапінг впаде ще до перевірки шляху.
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

        // Складання MapGroup("/api/genres") + MapGet("") формально дає RawText
        // "/api/genres/" — косметичний артефакт патерн-білдера. Маршрутизація кінцевий
        // слеш ігнорує; контрактний (і єдиний) шлях — /api/genres, його ж бачить
        // і Swagger (див. експортований swagger.json), і клієнт.
        var route = Assert.Single(endpoints, e =>
            (e.RoutePattern.RawText ?? string.Empty).TrimEnd('/') == "/api/genres");
        var methods = route.Metadata.OfType<HttpMethodMetadata>().SelectMany(m => m.HttpMethods);
        Assert.Contains("GET", methods);
        // Жодних інших маршрутів під /api/genres* бути не повинно.
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
