using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Core.Models.Entities;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// MediaService read/update/delete: maps entities to MediaResponseDto (sorts
/// genres/tags, uses lowercase type/status values, applies partial updates (null = unchanged),
/// and deletes sessions with their media item. Missing records return null/false without exceptions.
/// </summary>
public class MediaServiceCrudTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsMappedDto_WithSortedGenresAndTags()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var result = await service.GetByIdAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal(item.Id, result.Id);
        Assert.Equal("Mad Max: Fury Road", result.Title);
        Assert.Equal(2015, result.Year);
        Assert.Equal("movie", result.Type);
        Assert.Equal("watching", result.Status);
        Assert.Equal(8, result.Rating);
        Assert.Equal("76341", result.ExternalId);
        Assert.Equal("TMDB", result.ExternalSource);
        Assert.False(result.IsManualEntry);
        // Genres and tags are alphabetically sorted in the response, regardless of storage order.
        Assert.Equal(new[] { "Action", "Thriller" }, result.Genres);
        Assert.Equal(new[] { "favorite", "rewatch" }, result.Tags);
        Assert.Equal(new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc), result.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMissing_ReturnsNull()
    {
        using var db = CreateDb();
        var service = new MediaService(db, TestCurrentUser.Default);

        Assert.Null(await service.GetByIdAsync(999));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesOnlyProvidedFields_AndSetsUpdatedAt()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var updated = await service.UpdateAsync(item.Id, new UpdateMediaDto
        {
            Rating = 10,
            Status = "completed",
            Notes = "  finished  "
        });

        Assert.NotNull(updated);
        // Fields omitted from the DTO remain unchanged.
        Assert.Equal("Mad Max: Fury Road", updated.Title);
        Assert.Equal(2015, updated.Year);
        Assert.Equal("movie", updated.Type);
        Assert.Equal(new[] { "Action", "Thriller" }, updated.Genres);
        // Supplied fields are applied.
        Assert.Equal(10, updated.Rating);
        Assert.Equal("completed", updated.Status);
        Assert.Equal("finished", updated.Notes);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, updated.UpdatedAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc), updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesGenresAndTags_WhenProvided()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var updated = await service.UpdateAsync(item.Id, new UpdateMediaDto
        {
            Genres = new List<string> { "Drama", "Comedy" },
            Tags = new List<string> { "classic" }
        });

        Assert.NotNull(updated);
        Assert.Equal(new[] { "Comedy", "Drama" }, updated.Genres);
        Assert.Equal(new[] { "classic" }, updated.Tags);

        var reloaded = await service.GetByIdAsync(item.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(new[] { "Comedy", "Drama" }, reloaded.Genres);
        Assert.Equal(new[] { "classic" }, reloaded.Tags);
    }

    [Fact]
    public async Task UpdateAsync_EmptyGenreAndTagLists_ClearCollections()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var updated = await service.UpdateAsync(item.Id, new UpdateMediaDto
        {
            Genres = new List<string>(),
            Tags = new List<string>()
        });

        Assert.NotNull(updated);
        Assert.Empty(updated.Genres);
        Assert.Empty(updated.Tags);

        var reloaded = await service.GetByIdAsync(item.Id);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.Genres);
        Assert.Empty(reloaded.Tags);
    }

    [Fact]
    public async Task UpdateAsync_WhenMissing_ReturnsNullWithoutThrowing()
    {
        using var db = CreateDb();
        var service = new MediaService(db, TestCurrentUser.Default);

        var result = await service.UpdateAsync(999, new UpdateMediaDto { Rating = 5 });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_WhitespaceTitle_ThrowsArgumentException()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(item.Id, new UpdateMediaDto { Title = "   " }));

        Assert.Equal("Title is required", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_WithUnknownStatus_ThrowsArgumentException()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(item.Id, new UpdateMediaDto { Status = "paused" }));

        Assert.Contains("Invalid media status", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task UpdateAsync_RatingOutOfRange_ThrowsArgumentException(int rating)
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        var service = new MediaService(db, TestCurrentUser.Default);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(item.Id, new UpdateMediaDto { Rating = rating }));

        Assert.Contains("between 1 and 10", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_ExistingItem_RemovesItWithSessions_AndReturnsTrue()
    {
        using var db = CreateDb();
        var item = SeedMovie(db);
        db.ViewingSessions.Add(new ViewingSession
        {
            MediaItemId = item.Id,
            Date = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var service = new MediaService(db, TestCurrentUser.Default);
        // Load dependent sessions into the context so cascade behavior is deterministic
        // (InMemory does not enforce database-level foreign keys like production providers do).
        _ = await db.ViewingSessions.ToListAsync();

        var deleted = await service.DeleteAsync(item.Id);

        Assert.True(deleted);
        Assert.Equal(0, await db.MediaItems.CountAsync());
        Assert.Equal(0, await db.ViewingSessions.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WhenMissing_ReturnsFalse()
    {
        using var db = CreateDb();
        var service = new MediaService(db, TestCurrentUser.Default);

        Assert.False(await service.DeleteAsync(999));
    }

    private static MediaItem SeedMovie(AppDbContext db)
    {
        var item = new MediaItem
        {
            OwnerId = TestCurrentUser.Default.UserId,
            Title = "Mad Max: Fury Road",
            OriginalTitle = "Mad Max: Fury Road",
            Year = 2015,
            Type = MediaType.Movie,
            Rating = 8,
            Status = MediaStatus.Watching,
            ExternalId = "76341",
            ExternalSource = "TMDB",
            IsManualEntry = false,
            CreatedAt = new DateTime(2026, 10, 15, 10, 30, 0, DateTimeKind.Utc),
            Genres = new List<Genre>
            {
                new() { Name = "Thriller" },
                new() { Name = "Action" }
            },
            Tags = new List<Tag>
            {
                new() { Name = "rewatch" },
                new() { Name = "favorite" }
            }
        };

        db.MediaItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MediaServiceCrudTests-{Guid.NewGuid()}")
            .Options);
}
