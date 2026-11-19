using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Api.Security;
using MediaLibrary.Core.Data;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// GET /api/tags returns the authenticated owner's tag dictionary as { tags: [...] }.
/// </summary>
public class TagEndpointsTests
{
    [Fact]
    public async Task GetAll_ReturnsTagsSortedByName_WithContractShape()
    {
        using var db = CreateDb();
        var tags = new[]
        {
            new Tag { Name = "rewatch" },
            new Tag { Name = "favorite" }
        };
        SeedTagMedia(db, tags);

        var json = await ExecuteGetAllAsync(db);

        var entries = json.GetProperty("tags").EnumerateArray().ToList();
        Assert.Equal(new[] { "favorite", "rewatch" },
            entries.Select(tag => tag.GetProperty("name").GetString()).ToArray());
        Assert.All(entries, tag =>
            Assert.Equal(new[] { "id", "name" }, tag.EnumerateObject().Select(p => p.Name).ToArray()));
    }

    [Fact]
    public async Task GetAll_HidesTagsUsedOnlyByOtherOwners()
    {
        using var db = CreateDb();
        SeedTagMedia(db, new[] { new Tag { Name = "personal" } }, ownerId: 101);
        SeedTagMedia(db, new[] { new Tag { Name = "private" } }, ownerId: 202);

        var json = await ExecuteGetAllAsync(db, new TestCurrentUser(101));

        var names = json.GetProperty("tags").EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(new[] { "personal" }, names);
    }

    [Fact]
    public void MapTagEndpoints_MapsExactlyTheContractRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"TagRouteTests-{Guid.NewGuid()}"));
        builder.Services.AddSingleton<ICurrentUser>(TestCurrentUser.Default);
        builder.Services.AddAuthorization();
        using var app = builder.Build();

        app.MapTagEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var route = Assert.Single(endpoints, endpoint =>
            (endpoint.RoutePattern.RawText ?? string.Empty).TrimEnd('/') == "/api/tags");
        Assert.Contains("GET", route.Metadata.OfType<HttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods));
        Assert.Single(endpoints, endpoint =>
            (endpoint.RoutePattern.RawText ?? string.Empty).StartsWith("/api/tags", StringComparison.Ordinal));
    }

    private static async Task<JsonElement> ExecuteGetAllAsync(
        AppDbContext db,
        TestCurrentUser? currentUser = null)
    {
        var handler = typeof(TagEndpoints).GetMethod("GetAll", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TagEndpoints.GetAll not found.");

        var result = await (Task<IResult>)handler.Invoke(
            null,
            new object[] { db, currentUser ?? TestCurrentUser.Default })!;

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

    private static void SeedTagMedia(AppDbContext db, IEnumerable<Tag> tags, int ownerId = 1)
    {
        db.MediaItems.Add(new MediaItem
        {
            OwnerId = ownerId,
            Title = "Fixture",
            OriginalTitle = "Fixture",
            Type = MediaType.Movie,
            Status = MediaStatus.Plan,
            CreatedAt = DateTime.UtcNow,
            Tags = tags.ToList()
        });
        db.SaveChanges();
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TagEndpointsTests-{Guid.NewGuid()}")
            .Options);
}
