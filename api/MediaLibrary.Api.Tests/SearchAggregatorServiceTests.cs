using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Core.Data;
using MediaLibrary.Core.Models.Entities;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// Search pipeline database → TMDB → RAWG: contract 503 responses when a required
/// external provider is unavailable, result merging and deduplication by
/// ExternalSource + ExternalId, type filtering, and catalog genre lookup for imports.
/// HTTP dependencies use a stub HttpMessageHandler; the database uses InMemory.
/// </summary>
public class SearchAggregatorServiceTests
{
    [Fact]
    public async Task SearchAsync_WhenQueryIsBlank_ReturnsEmptyWithoutCallingProviders()
    {
        var tmdbCalls = 0;
        var rawgCalls = 0;
        using var db = CreateDb();
        SeedItem(db, "Inception");

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: _ => { tmdbCalls++; return Json("{\"results\":[]}"); },
            rawgResponder: _ => { rawgCalls++; return Json("{\"results\":[]}"); });

        var results = await aggregator.SearchAsync("   ", null);

        Assert.Empty(results);
        Assert.Equal(0, tmdbCalls);
        Assert.Equal(0, rawgCalls);
    }

    [Fact]
    public async Task SearchAsync_WithInvalidType_ThrowsArgumentException()
    {
        using var db = CreateDb();
        var aggregator = CreateAggregator(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => aggregator.SearchAsync("Inception", "anime"));

        Assert.Contains("Invalid media type", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_WithoutExternalApiKeys_ThrowsUnavailableEvenWhenLocalResultsExist()
    {
        using var db = CreateDb();
        SeedItem(db, "Inception", MediaType.Movie, externalId: "27205", externalSource: "TMDB");

        var aggregator = CreateAggregator(db, configuredKeys: false);

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(
            () => aggregator.SearchAsync("Inception", null));
    }

    [Fact]
    public async Task SearchAsync_WhenSelectedExternalProviderFails_ThrowsUnavailable()
    {
        using var db = CreateDb();
        SeedItem(db, "Blade Runner 2049", MediaType.Movie);

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: _ => throw new HttpRequestException("TMDB is down"));

        await Assert.ThrowsAsync<ExternalApiUnavailableException>(
            () => aggregator.SearchAsync("Blade", "movie"));
    }

    [Fact]
    public async Task SearchAsync_MergesAllSources_AndSkipsDuplicatesByExternalKey()
    {
        using var db = CreateDb();
        SeedItem(db, "Inception (local copy)", MediaType.Movie, externalId: "27205", externalSource: "TMDB");

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: request =>
                request.RequestUri!.AbsolutePath.Contains("search/movie")
                    ? Json("""
                           {"results":[
                             {"id":27205,"title":"Inception","release_date":"2010-07-16","poster_path":"/p1.jpg"},
                             {"id":238,"title":"The Dark Knight","release_date":"2008-07-18"}
                           ]}
                           """)
                    : Json("{\"results\":[]}"),
            rawgResponder: _ => Json("""
                                     {"results":[{"id":3498,"name":"Grand Theft Auto V","released":"2013-09-17"}]}
                                     """));

        var results = await aggregator.SearchAsync("Inception", null);

        Assert.Equal(3, results.Count);
        // The local record is first and wins deduplication against its external match.
        Assert.Equal("Inception (local copy)", results[0].Title);
        Assert.Single(results, r => r.ExternalId == "27205");
        Assert.Contains(results, r => r.ExternalId == "238" && r.ExternalSource == "TMDB" && r.Year == 2008);
        Assert.Contains(results, r => r.ExternalId == "3498" && r.ExternalSource == "RAWG" && r.Year == 2013);
    }

    [Fact]
    public async Task SearchAsync_Deduplicates_IgnoringExternalSourceCase()
    {
        using var db = CreateDb();
        SeedItem(db, "Inception", MediaType.Movie, externalId: "27205", externalSource: "tmdb");

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: _ => Json("""
                                     {"results":[{"id":27205,"title":"Inception","release_date":"2010-07-16"}]}
                                     """));

        var results = await aggregator.SearchAsync("Inception", "movie");

        // The external "TMDB|27205" matches the local "tmdb|27205" case-insensitively.
        var only = Assert.Single(results);
        Assert.Equal("tmdb", only.ExternalSource);
    }

    [Fact]
    public async Task SearchAsync_WithTypeMovie_QueriesOnlyTmdbMovies()
    {
        var moviesCalled = false;
        var seriesCalled = false;
        var rawgCalled = false;
        using var db = CreateDb();

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("search/movie"))
                {
                    moviesCalled = true;
                    return Json("""
                                {"results":[{"id":27205,"title":"Inception","release_date":"2010-07-16","poster_path":"/p1.jpg"}]}
                                """);
                }

                seriesCalled = true;
                return Json("{\"results\":[]}");
            },
            rawgResponder: _ =>
            {
                rawgCalled = true;
                return Json("{\"results\":[]}");
            });

        var results = await aggregator.SearchAsync("Inception", "movie");

        Assert.True(moviesCalled);
        Assert.False(seriesCalled);
        Assert.False(rawgCalled);
        var movie = Assert.Single(results);
        Assert.Equal("movie", movie.Type);
        // The poster URL combines the TMDB path with the default ImageBaseUrl.
        Assert.Equal("https://image.tmdb.org/t/p/w500/p1.jpg", movie.PosterUrl);
    }

    [Fact]
    public async Task SearchAsync_WithTypeGame_QueriesOnlyRawg()
    {
        var tmdbCalled = false;
        var rawgCalled = false;
        using var db = CreateDb();

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: _ =>
            {
                tmdbCalled = true;
                return Json("{\"results\":[]}");
            },
            rawgResponder: _ =>
            {
                rawgCalled = true;
                return Json("""
                            {"results":[{"id":3328,"name":"The Witcher 3: Wild Hunt","released":"2015-05-18"}]}
                            """);
            });

        var results = await aggregator.SearchAsync("Witcher", "game");

        Assert.False(tmdbCalled);
        Assert.True(rawgCalled);
        var game = Assert.Single(results);
        Assert.Equal("game", game.Type);
        Assert.Equal("RAWG", game.ExternalSource);
    }

    [Fact]
    public async Task SearchAsync_ManualLibraryItem_IsReportedWithManualSourceAndLocalId()
    {
        using var db = CreateDb();
        var item = SeedItem(db, "Home Video 2024", MediaType.Movie);

        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: _ => Json("{\"results\":[]}"),
            rawgResponder: _ => Json("{\"results\":[]}"));

        var results = await aggregator.SearchAsync("Home Video", null);

        var local = Assert.Single(results);
        Assert.Equal("Manual", local.ExternalSource);
        Assert.Equal(item.Id.ToString(), local.ExternalId);
        Assert.Equal(item.Title, local.Title);
    }

    [Fact]
    public async Task FetchImportGenresAsync_ForTmdbMovie_UsesMovieDetailsEndpoint()
    {
        using var db = CreateDb();
        string? requestedPath = null;
        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            tmdbResponder: request =>
            {
                requestedPath = request.RequestUri!.AbsolutePath;
                return Json("""
                           {"genres":[{"id":878,"name":"Science Fiction"},{"id":53,"name":"Thriller"}]}
                           """);
            });

        var genres = await aggregator.FetchImportGenresAsync(new ImportMediaDto
        {
            ExternalId = "27205",
            ExternalSource = "TMDB",
            Title = "Inception",
            Type = "movie"
        });

        Assert.Equal("/3/movie/27205", requestedPath);
        Assert.Equal(new[] { "Science Fiction", "Thriller" }, genres);
    }

    [Fact]
    public async Task FetchImportGenresAsync_ForRawgGame_UsesGameDetailsEndpoint()
    {
        using var db = CreateDb();
        string? requestedPath = null;
        var aggregator = CreateAggregator(
            db,
            configuredKeys: true,
            rawgResponder: request =>
            {
                requestedPath = request.RequestUri!.AbsolutePath;
                return Json("""
                           {"genres":[{"id":5,"name":"RPG"},{"id":3,"name":"Adventure"}]}
                           """);
            });

        var genres = await aggregator.FetchImportGenresAsync(new ImportMediaDto
        {
            ExternalId = "3328",
            ExternalSource = "RAWG",
            Title = "The Witcher 3",
            Type = "game"
        });

        Assert.Equal("/api/games/3328", requestedPath);
        Assert.Equal(new[] { "RPG", "Adventure" }, genres);
    }

    [Fact]
    public async Task FetchImportGenresAsync_RejectsMismatchedSourceAndType()
    {
        using var db = CreateDb();
        var aggregator = CreateAggregator(db, configuredKeys: true);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            aggregator.FetchImportGenresAsync(new ImportMediaDto
            {
                ExternalId = "3328",
                ExternalSource = "RAWG",
                Title = "The Witcher 3",
                Type = "movie"
            }));
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"SearchAggregatorTests-{Guid.NewGuid()}")
            .Options);

    private static MediaItem SeedItem(
        AppDbContext db,
        string title,
        MediaType type = MediaType.Movie,
        string? externalId = null,
        string? externalSource = null)
    {
        var item = new MediaItem
        {
            OwnerId = TestCurrentUser.Default.UserId,
            Title = title,
            OriginalTitle = title,
            Type = type,
            Status = MediaStatus.Plan,
            ExternalId = externalId,
            ExternalSource = externalSource,
            IsManualEntry = externalId is null && externalSource is null,
            CreatedAt = DateTime.UtcNow
        };

        db.MediaItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static SearchAggregatorService CreateAggregator(
        AppDbContext db,
        bool configuredKeys = false,
        Func<HttpRequestMessage, HttpResponseMessage>? tmdbResponder = null,
        Func<HttpRequestMessage, HttpResponseMessage>? rawgResponder = null)
    {
        var media = new MediaService(db, TestCurrentUser.Default);

        var tmdb = new TmdbService(
            tmdbResponder is null ? new HttpClient() : new HttpClient(new StubHttpMessageHandler(tmdbResponder)),
            BuildConfig("Tmdb", configuredKeys),
            NullLogger<TmdbService>.Instance);

        var rawg = new RawgService(
            rawgResponder is null ? new HttpClient() : new HttpClient(new StubHttpMessageHandler(rawgResponder)),
            BuildConfig("Rawg", configuredKeys),
            NullLogger<RawgService>.Instance);

        return new SearchAggregatorService(media, tmdb, rawg);
    }

    private static IConfiguration BuildConfig(string section, bool withKey)
    {
        var values = new Dictionary<string, string?>();
        if (withKey)
            values[$"{section}:ApiKey"] = "test-key";

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    /// <summary>Stub HttpClient transport whose responses are controlled by the test.</summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
