using MediaLibrary.Api.Data;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Api.Models.Entities;
using MediaLibrary.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// Сесії перегляду (/api/media/{id}/sessions): читання відсортоване за датою
/// (новіші перші), створення за замовчуванням бере поточний UTC-час, коли Date
/// не передано. Відсутнє медіа → null (ендпоінт мапить у 404 MEDIA_NOT_FOUND).
/// </summary>
public class MediaServiceSessionsTests
{
    [Fact]
    public async Task GetSessionsAsync_WhenMediaMissing_ReturnsNull()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        Assert.Null(await service.GetSessionsAsync(999));
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsSessionsOrderedByDateDesc_ThenIdDesc()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var firstDay = Session(item.Id, new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc));
        var latestDay = Session(item.Id, new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc));
        var tieEarlier = Session(item.Id, new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc));
        var tieLater = Session(item.Id, new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc));
        db.ViewingSessions.AddRange(firstDay, latestDay, tieEarlier, tieLater);
        await db.SaveChangesAsync();

        var service = new MediaService(db);
        var sessions = await service.GetSessionsAsync(item.Id);

        Assert.NotNull(sessions);
        Assert.Equal(
            new[] { latestDay.Id, tieLater.Id, tieEarlier.Id, firstDay.Id },
            sessions.Select(s => s.Id));
    }

    [Fact]
    public async Task GetSessionsAsync_WithoutSessions_ReturnsEmptyList()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var service = new MediaService(db);

        var sessions = await service.GetSessionsAsync(item.Id);

        Assert.NotNull(sessions);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task GetSessionsAsync_ReturnsOnlySessionsOfRequestedMedia()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var other = SeedItem(db, "Other Title");
        db.ViewingSessions.AddRange(
            Session(item.Id, new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc)),
            Session(other.Id, new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var service = new MediaService(db);
        var sessions = await service.GetSessionsAsync(item.Id);

        Assert.NotNull(sessions);
        var session = Assert.Single(sessions);
        Assert.Equal(item.Id, session.MediaItemId);
    }

    [Fact]
    public async Task AddSessionAsync_WhenMediaMissing_ReturnsNull()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var result = await service.AddSessionAsync(999, new CreateViewingSessionDto());

        Assert.Null(result);
        Assert.Equal(0, await db.ViewingSessions.CountAsync());
    }

    [Fact]
    public async Task AddSessionAsync_WithoutDate_UsesCurrentUtcTime()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var service = new MediaService(db);

        var before = DateTime.UtcNow.AddSeconds(-5);
        var result = await service.AddSessionAsync(item.Id, new CreateViewingSessionDto
        {
            Note = "pilot",
            Progress = "S1E1"
        });
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal(item.Id, result.MediaItemId);
        Assert.Equal(DateTimeKind.Utc, result.Date.Kind);
        Assert.InRange(result.Date, before, after);
        Assert.Equal("pilot", result.Note);
        Assert.Equal("S1E1", result.Progress);
    }

    [Fact]
    public async Task AddSessionAsync_WithProvidedDate_KeepsValue_AndMarksItUtc()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var service = new MediaService(db);
        var provided = new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Unspecified);

        var result = await service.AddSessionAsync(item.Id, new CreateViewingSessionDto
        {
            Date = provided
        });

        Assert.NotNull(result);
        Assert.Equal(provided, result.Date);
        // Kind = Utc гарантує суфікс Z у JSON-відповіді (ISO 8601 за контрактом).
        Assert.Equal(DateTimeKind.Utc, result.Date.Kind);
    }

    [Fact]
    public async Task AddSessionAsync_TrimsNote_WhitespaceBecomesNull()
    {
        using var db = CreateDb();
        var item = SeedItem(db);
        var service = new MediaService(db);

        var trimmed = await service.AddSessionAsync(item.Id, new CreateViewingSessionDto
        {
            Note = "  rewatch with friends  ",
            Progress = "  S2  "
        });

        Assert.NotNull(trimmed);
        Assert.Equal("rewatch with friends", trimmed.Note);
        Assert.Equal("S2", trimmed.Progress);

        var blanked = await service.AddSessionAsync(item.Id, new CreateViewingSessionDto
        {
            Note = "   "
        });

        Assert.NotNull(blanked);
        Assert.Null(blanked.Note);
        Assert.Null(blanked.Progress);
    }

    private static MediaItem SeedItem(AppDbContext db, string title = "Dark")
    {
        var item = new MediaItem
        {
            Title = title,
            OriginalTitle = title,
            Type = MediaType.Series,
            Status = MediaStatus.Watching,
            CreatedAt = DateTime.UtcNow
        };

        db.MediaItems.Add(item);
        db.SaveChanges();
        return item;
    }

    private static ViewingSession Session(int mediaItemId, DateTime date) =>
        new()
        {
            MediaItemId = mediaItemId,
            Date = date
        };

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MediaServiceSessionsTests-{Guid.NewGuid()}")
            .Options);
}
