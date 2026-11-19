using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Services;
using MediaLibrary.Core.Data;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Tests;

/// <summary>Checks that CRUD, local search, imports, and viewing sessions stay in the caller's library.</summary>
public sealed class MediaServiceOwnerIsolationTests
{
    private const int AliceId = 101;
    private const int BobId = 202;

    [Fact]
    public async Task GetFilteredAsync_ReturnsOnlyCurrentOwnersRowsAndCount()
    {
        using var db = CreateDb();
        var alice = SeedMedia(db, AliceId, "Shared title");
        SeedMedia(db, BobId, "Shared title");
        SeedMedia(db, BobId, "Another title");

        var result = await Service(db, AliceId).GetFilteredAsync(new MediaFilter());

        Assert.Equal(1, result.Total);
        Assert.Equal(0, result.Offset);
        Assert.Equal(20, result.Limit);
        Assert.Collection(result.Items, item => Assert.Equal(alice.Id, item.Id));
    }

    [Fact]
    public async Task GetFilteredAsync_SearchNeverReturnsAnotherOwnersMatchingTitle()
    {
        using var db = CreateDb();
        var alice = SeedMedia(db, AliceId, "Moonlight");
        SeedMedia(db, BobId, "Moonlight");

        var result = await Service(db, AliceId).GetFilteredAsync(new MediaFilter { Search = "Moon" });

        Assert.Equal(1, result.Total);
        Assert.Equal(alice.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetFilteredAsync_PaginationTotalsOnlyIncludeCurrentOwner()
    {
        using var db = CreateDb();
        var baseTime = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var older = SeedMedia(db, AliceId, "Older", baseTime);
        SeedMedia(db, AliceId, "Newer", baseTime.AddMinutes(1));
        SeedMedia(db, BobId, "Other owner's newest", baseTime.AddMinutes(2));

        var page = await Service(db, AliceId).GetFilteredAsync(new MediaFilter { Offset = 1, Limit = 1 });

        Assert.Equal(2, page.Total);
        Assert.Equal(older.Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.Offset);
        Assert.Equal(1, page.Limit);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForAnotherOwnersRecord()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's private item");

        Assert.Null(await Service(db, AliceId).GetByIdAsync(bobItem.Id));
    }

    [Fact]
    public async Task CreateAsync_AlwaysAssignsOwnerFromAuthenticatedContext()
    {
        using var db = CreateDb();
        var created = await Service(db, AliceId).CreateAsync(new CreateMediaDto
        {
            Title = "Alice's new item",
            Type = "movie"
        });

        var stored = await db.MediaItems.SingleAsync();
        Assert.Equal(AliceId, stored.OwnerId);
        Assert.Equal(created.Id, stored.Id);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNullAndLeavesForeignRecordUnchanged()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's original");

        var updated = await Service(db, AliceId).UpdateAsync(bobItem.Id, new UpdateMediaDto
        {
            Title = "Attempted takeover"
        });

        Assert.Null(updated);
        Assert.Equal("Bob's original", (await db.MediaItems.SingleAsync()).Title);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNotFoundBeforeValidatingForeignRecords()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's original");

        var updated = await Service(db, AliceId).UpdateAsync(bobItem.Id, new UpdateMediaDto { Year = 1800 });

        Assert.Null(updated);
        Assert.Equal("Bob's original", (await db.MediaItems.SingleAsync()).Title);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseAndPreservesForeignRecord()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's item");

        var deleted = await Service(db, AliceId).DeleteAsync(bobItem.Id);

        Assert.False(deleted);
        Assert.Equal(bobItem.Id, (await db.MediaItems.SingleAsync()).Id);
    }

    [Fact]
    public async Task ImportFromExternalAsync_AllowsSameExternalKeyForDifferentOwnersOnly()
    {
        using var db = CreateDb();
        var dto = ImportDto("same-id", "TMDB");
        var aliceImport = await Service(db, AliceId).ImportFromExternalAsync(dto);
        var bobImport = await Service(db, BobId).ImportFromExternalAsync(dto);

        Assert.NotEqual(aliceImport.Id, bobImport.Id);
        Assert.Equal(2, await db.MediaItems.CountAsync());
        Assert.Equal(AliceId, (await db.MediaItems.SingleAsync(item => item.Id == aliceImport.Id)).OwnerId);
        Assert.Equal(BobId, (await db.MediaItems.SingleAsync(item => item.Id == bobImport.Id)).OwnerId);
    }

    [Fact]
    public async Task ImportFromExternalAsync_DeduplicatesWithinTheSameOwner()
    {
        using var db = CreateDb();
        await Service(db, AliceId).ImportFromExternalAsync(ImportDto("duplicate", "TMDB"));

        await Assert.ThrowsAsync<DuplicateException>(() =>
            Service(db, AliceId).ImportFromExternalAsync(ImportDto("duplicate", "TMDB")));

        Assert.Equal(1, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task ImportFromExternalAsync_DoesNotTreatAnotherOwnersImportAsDuplicate()
    {
        using var db = CreateDb();
        await Service(db, BobId).ImportFromExternalAsync(ImportDto("external-42", "RAWG"));

        var imported = await Service(db, AliceId).ImportFromExternalAsync(ImportDto("external-42", "RAWG"));

        Assert.Equal(AliceId, (await db.MediaItems.SingleAsync(item => item.Id == imported.Id)).OwnerId);
        Assert.Equal(2, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsNullForAnotherOwnersMedia()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's series");
        db.ViewingSessions.Add(new ViewingSession { MediaItemId = bobItem.Id, Date = DateTime.UtcNow });
        await db.SaveChangesAsync();

        Assert.Null(await Service(db, AliceId).GetSessionsAsync(bobItem.Id));
    }

    [Fact]
    public async Task AddSessionAsync_CannotAppendToAnotherOwnersMedia()
    {
        using var db = CreateDb();
        var bobItem = SeedMedia(db, BobId, "Bob's series");

        var result = await Service(db, AliceId).AddSessionAsync(bobItem.Id, new CreateViewingSessionDto
        {
            Note = "unauthorized write"
        });

        Assert.Null(result);
        Assert.Empty(await db.ViewingSessions.ToListAsync());
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsOnlyTheCurrentOwnersSessions()
    {
        using var db = CreateDb();
        var aliceItem = SeedMedia(db, AliceId, "Alice's series");
        var bobItem = SeedMedia(db, BobId, "Bob's series");
        var ownSession = new ViewingSession { MediaItemId = aliceItem.Id, Date = DateTime.UtcNow };
        db.ViewingSessions.AddRange(
            ownSession,
            new ViewingSession { MediaItemId = bobItem.Id, Date = DateTime.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();

        var result = await Service(db, AliceId).GetSessionsAsync(aliceItem.Id);

        Assert.Equal(ownSession.Id, Assert.Single(result!).Id);
    }

    [Fact]
    public async Task SearchAggregator_UsesOnlyTheCurrentOwnersLocalRecords()
    {
        using var db = CreateDb();
        var ownItem = SeedMedia(db, AliceId, "Private Moon");
        SeedMedia(db, BobId, "Private Moon");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tmdb:ApiKey"] = "test-key",
                ["Rawg:ApiKey"] = "test-key"
            })
            .Build();
        var media = Service(db, AliceId);
        var tmdb = new TmdbService(
            new HttpClient(new EmptyCatalogHandler()),
            configuration,
            NullLogger<TmdbService>.Instance);
        var rawg = new RawgService(
            new HttpClient(new EmptyCatalogHandler()),
            configuration,
            NullLogger<RawgService>.Instance);
        var search = new SearchAggregatorService(media, tmdb, rawg);

        var results = await search.SearchAsync("Private Moon", null);

        var result = Assert.Single(results);
        Assert.Equal(ownItem.ExternalId ?? ownItem.Id.ToString(), result.ExternalId);
        Assert.Equal(ownItem.Title, result.Title);
        Assert.Equal("Manual", result.ExternalSource);
    }

    private static MediaItem SeedMedia(AppDbContext db, int ownerId, string title, DateTime? createdAt = null)
    {
        var item = new MediaItem
        {
            OwnerId = ownerId,
            Title = title,
            OriginalTitle = title,
            Type = MediaType.Movie,
            Status = MediaStatus.Plan,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsManualEntry = true,
            ExternalSource = "Manual"
        };
        db.MediaItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static ImportMediaDto ImportDto(string externalId, string source) => new()
    {
        ExternalId = externalId,
        ExternalSource = source,
        Title = "Imported",
        Type = "movie"
    };

    private static MediaService Service(AppDbContext db, int ownerId) =>
        new(db, new TestCurrentUser(ownerId));

    private sealed class EmptyCatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}", Encoding.UTF8, "application/json")
            });
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MediaServiceOwnerIsolationTests-{Guid.NewGuid()}")
            .Options);
}
