using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Endpoints;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// GET /api/stats: aggregates counts by status/type (contract lowercase keys),
/// averages only non-null ratings (rounded to one decimal place; 0 when no ratings exist),
/// and returns up to five topGenres. An empty database produces a valid zero-valued response.
/// Aggregation is implemented in StatsEndpoints, so tests call the handler directly
/// and inspect the actual JSON returned to an API client.
/// </summary>
public class StatsServiceTests
{
    [Fact]
    public async Task GetStats_EmptyDatabase_ReturnsZeroedButValidContract()
    {
        using var db = CreateDb();

        var json = await ExecuteGetStatsAsync(db);

        Assert.Equal(0, json.GetProperty("total").GetInt32());
        Assert.Equal(0, json.GetProperty("byStatus").GetArrayLength());
        Assert.Equal(0, json.GetProperty("byType").GetArrayLength());
        Assert.Equal(0, json.GetProperty("avgRating").GetDouble());
        Assert.Equal(0, json.GetProperty("topGenres").GetArrayLength());
    }

    [Fact]
    public async Task GetStats_CountsByStatusAndType_WithLowercaseContractKeys()
    {
        using var db = CreateDb();
        SeedItem(db, "m1", MediaType.Movie, MediaStatus.Completed);
        SeedItem(db, "m2", MediaType.Movie, MediaStatus.Plan);
        SeedItem(db, "s1", MediaType.Series, MediaStatus.Watching);
        SeedItem(db, "g1", MediaType.Game, MediaStatus.Completed);

        var json = await ExecuteGetStatsAsync(db);

        Assert.Equal(4, json.GetProperty("total").GetInt32());

        var byStatus = ToCountMap(json.GetProperty("byStatus"), "status");
        Assert.Equal(2, byStatus["completed"]);
        Assert.Equal(1, byStatus["plan"]);
        Assert.Equal(1, byStatus["watching"]);
        Assert.False(byStatus.ContainsKey("dropped")); // Missing statuses are omitted.

        var byType = ToCountMap(json.GetProperty("byType"), "type");
        Assert.Equal(2, byType["movie"]);
        Assert.Equal(1, byType["series"]);
        Assert.Equal(1, byType["game"]);
    }

    [Fact]
    public async Task GetStats_AvgRating_UsesOnlyRatedItems_RoundedToOneDecimal()
    {
        using var db = CreateDb();
        SeedItem(db, "a", rating: 8);
        SeedItem(db, "b", rating: 9);
        SeedItem(db, "c", rating: 8);
        SeedItem(db, "unrated");

        var json = await ExecuteGetStatsAsync(db);

        // (8 + 9 + 8) / 3 = 8.333… → 8.3; an unrated item does not affect the average.
        Assert.Equal(8.3, json.GetProperty("avgRating").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task GetStats_WithoutAnyRatings_AvgRatingIsZeroNotNull()
    {
        using var db = CreateDb();
        SeedItem(db, "a");
        SeedItem(db, "b");

        var json = await ExecuteGetStatsAsync(db);

        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Number, json.GetProperty("avgRating").ValueKind);
        Assert.Equal(0, json.GetProperty("avgRating").GetDouble());
    }

    [Fact]
    public async Task GetStats_TopGenres_TakesFiveMostUsed_OrderedByCountThenName()
    {
        using var db = CreateDb();
        var genres = new Dictionary<string, Genre>();
        Genre G(string name)
        {
            if (!genres.TryGetValue(name, out var genre))
                genres[name] = genre = new Genre { Name = name };
            return genre;
        }

        SeedItem(db, "i1", genres: new[] { G("Action"), G("Comedy") });
        SeedItem(db, "i2", genres: new[] { G("Action"), G("Drama") });
        SeedItem(db, "i3", genres: new[] { G("Action"), G("Comedy") });
        SeedItem(db, "i4", genres: new[] { G("Drama"), G("Horror"), G("Sci-Fi") });
        SeedItem(db, "i5", genres: new[] { G("Horror"), G("Thriller"), G("Western") });

        var json = await ExecuteGetStatsAsync(db);

        var top = json.GetProperty("topGenres").EnumerateArray()
            .Select(g => (Name: g.GetProperty("name").GetString()!, Count: g.GetProperty("count").GetInt32()))
            .ToList();

        // With a limit of five, Sci-Fi (1) is included while Thriller/Western (1) are not.
        Assert.Equal(5, top.Count);
        Assert.Equal(("Action", 3), top[0]);
        Assert.Equal(("Comedy", 2), top[1]);
        Assert.Equal(("Drama", 2), top[2]);
        Assert.Equal(("Horror", 2), top[3]);
        Assert.Equal(("Sci-Fi", 1), top[4]);
    }

    [Fact]
    public async Task GetStats_OnlyCountsAuthenticatedOwnersLibrary()
    {
        using var db = CreateDb();
        SeedItem(db, "Alice rated", status: MediaStatus.Completed, rating: 10, ownerId: 101);
        SeedItem(db, "Alice unrated", type: MediaType.Series, ownerId: 101);
        SeedItem(db, "Bob rated", status: MediaStatus.Dropped, rating: 1, ownerId: 202);

        var json = await ExecuteGetStatsAsync(db, new TestCurrentUser(101));

        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(10, json.GetProperty("avgRating").GetDouble());
        Assert.Equal(2, json.GetProperty("byStatus").EnumerateArray().Sum(row => row.GetProperty("count").GetInt32()));
        Assert.Equal(2, json.GetProperty("byType").EnumerateArray().Sum(row => row.GetProperty("count").GetInt32()));
        Assert.DoesNotContain(
            json.GetProperty("byStatus").EnumerateArray(),
            row => row.GetProperty("status").GetString() == "dropped");
    }

    [Fact]
    public async Task GetStats_TopGenresDoesNotRevealGenresUsedOnlyByAnotherOwner()
    {
        using var db = CreateDb();
        var sharedGenre = new Genre { Name = "Drama" };
        var privateGenre = new Genre { Name = "Secret Genre" };
        SeedItem(db, "Alice", genres: new[] { sharedGenre }, ownerId: 101);
        SeedItem(db, "Bob", genres: new[] { privateGenre }, ownerId: 202);

        var json = await ExecuteGetStatsAsync(db, new TestCurrentUser(101));

        var topGenres = json.GetProperty("topGenres").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(new[] { "Drama" }, topGenres);
    }

    private static MediaItem SeedItem(
        AppDbContext db,
        string title,
        MediaType type = MediaType.Movie,
        MediaStatus status = MediaStatus.Plan,
        int? rating = null,
        IEnumerable<Genre>? genres = null,
        int ownerId = 1)
    {
        var item = new MediaItem
        {
            OwnerId = ownerId,
            Title = title,
            OriginalTitle = title,
            Type = type,
            Status = status,
            Rating = rating,
            CreatedAt = DateTime.UtcNow,
            Genres = genres?.ToList() ?? new List<Genre>()
        };

        db.MediaItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static Dictionary<string, int> ToCountMap(JsonElement array, string keyProperty)
    {
        var map = new Dictionary<string, int>();
        foreach (var row in array.EnumerateArray())
            map[row.GetProperty(keyProperty).GetString()!] = row.GetProperty("count").GetInt32();
        return map;
    }

    private static async Task<JsonElement> ExecuteGetStatsAsync(
        AppDbContext db,
        TestCurrentUser? currentUser = null)
    {
        var handler = typeof(StatsEndpoints).GetMethod("GetStats", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("StatsEndpoints.GetStats not found.");

        var result = await (Task<IResult>)handler.Invoke(null, new object[] { db, currentUser ?? TestCurrentUser.Default })!;
        return await ExecuteJsonAsync(result);
    }

    private static async Task<JsonElement> ExecuteJsonAsync(IResult result)
    {
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
            .UseInMemoryDatabase($"StatsServiceTests-{Guid.NewGuid()}")
            .Options);
}
