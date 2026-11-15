using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediaLibrary.Api.Data;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Models.Entities;

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
        db.Genres.AddRange(
            new Genre { Name = "Thriller" },
            new Genre { Name = "Action" },
            new Genre { Name = "Sci-Fi" });
        await db.SaveChangesAsync();

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
        db.Genres.Add(new Genre { Name = "Action" });
        await db.SaveChangesAsync();
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
        Assert.Single(endpoints.Where(e =>
            (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/genres", StringComparison.Ordinal)));
    }

    private static async Task<JsonElement> ExecuteGetAllAsync(AppDbContext db)
    {
        var handler = typeof(GenreEndpoints).GetMethod("GetAll", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GenreEndpoints.GetAll not found.");

        var result = await (Task<IResult>)handler.Invoke(null, new object[] { db })!;

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

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"GenreEndpointsTests-{Guid.NewGuid()}")
            .Options);
}
