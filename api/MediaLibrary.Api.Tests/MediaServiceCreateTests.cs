using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Core.Models.Entities;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// MediaService.CreateAsync: мапінг CreateMediaDto → MediaItem (trim полів, парсинг
/// рядкового type у enum, дефолтний status = plan), розв'язання жанрів/тегів через
/// get-or-create та портова валідація вхідних даних (ArgumentException → 400).
/// </summary>
public class MediaServiceCreateTests
{
    [Fact]
    public async Task CreateAsync_MapsDtoToEntity_AndReturnsContractDto()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto
        {
            Title = "  Inception  ",
            Year = 2010,
            Type = "MOVIE",
            PosterUrl = "  https://image.tmdb.org/t/p/w500/inception.jpg  ",
            Overview = "  A thief who steals corporate secrets.  ",
            Genres = new List<string> { "Sci-Fi" },
            Tags = new List<string> { "favorite" },
            Rating = 9,
            Status = "completed",
            Notes = "  worth rewatching  "
        };

        var result = await service.CreateAsync(dto);

        Assert.True(result.Id > 0);
        Assert.Equal("Inception", result.Title);
        Assert.Equal("Inception", result.OriginalTitle); // fallback на Title
        Assert.Equal(2010, result.Year);
        Assert.Equal("movie", result.Type); // контрактний lowercase
        Assert.Equal("https://image.tmdb.org/t/p/w500/inception.jpg", result.PosterUrl);
        Assert.Equal("A thief who steals corporate secrets.", result.Overview);
        Assert.Equal(9, result.Rating);
        Assert.Equal("completed", result.Status);
        Assert.Equal("worth rewatching", result.Notes);
        Assert.True(result.IsManualEntry);
        Assert.Null(result.ExternalId);
        Assert.Equal("Manual", result.ExternalSource);
        Assert.Equal(new[] { "Sci-Fi" }, result.Genres);
        Assert.Equal(new[] { "favorite" }, result.Tags);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Null(result.UpdatedAt);

        Assert.Equal(1, await db.MediaItems.CountAsync());
    }

    [Theory]
    [InlineData("sErIeS", "series")]
    [InlineData(" game ", "game")]
    [InlineData("Movie", "movie")]
    public async Task CreateAsync_ParsesTypeCaseInsensitively(string input, string expected)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var result = await service.CreateAsync(new CreateMediaDto { Title = "T", Type = input });

        Assert.Equal(expected, result.Type);
    }

    [Fact]
    public async Task CreateAsync_WithoutStatus_DefaultsToPlan()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var result = await service.CreateAsync(new CreateMediaDto
        {
            Title = "T",
            Type = "movie",
            Status = null!
        });

        Assert.Equal("plan", result.Status);
        var stored = await db.MediaItems.SingleAsync();
        Assert.Equal(MediaStatus.Plan, stored.Status);
    }

    [Fact]
    public async Task CreateAsync_WithoutGenresAndTags_LeavesCollectionsEmpty()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var result = await service.CreateAsync(new CreateMediaDto { Title = "T", Type = "movie" });

        Assert.Empty(result.Genres);
        Assert.Empty(result.Tags);
        Assert.Equal(0, await db.Genres.CountAsync());
        Assert.Equal(0, await db.Tags.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_MissingTitle_ThrowsArgumentException(string title)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto { Title = title, Type = "movie" };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(dto));

        Assert.Equal("Title is required", ex.Message);
        Assert.Empty(await db.MediaItems.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateAsync_MissingType_ThrowsArgumentException(string? type)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto { Title = "T", Type = type! };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(dto));

        Assert.Equal("Type is required", ex.Message);
    }

    [Theory]
    [InlineData("documentary")]
    [InlineData("film")]
    public async Task CreateAsync_WithUnknownType_ThrowsArgumentException(string type)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto { Title = "T", Type = type };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(dto));

        Assert.Contains("Invalid media type", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task CreateAsync_RatingOutOfRange_ThrowsArgumentException(int rating)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto { Title = "T", Type = "movie", Rating = rating };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(dto));

        Assert.Contains("between 1 and 10", ex.Message);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public async Task CreateAsync_YearOutOfRange_ThrowsArgumentException(int year)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new CreateMediaDto { Title = "T", Type = "movie", Year = year };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(dto));

        // Повідомлення генерує DataAnnotations RangeAttribute.
        Assert.Contains("Year", ex.Message);
        Assert.Contains("1900", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ReusesExistingGenres_IgnoringCase()
    {
        using var db = CreateDb();
        db.Genres.Add(new Genre { Name = "Sci-Fi" });
        await db.SaveChangesAsync();
        var service = new MediaService(db);

        var result = await service.CreateAsync(new CreateMediaDto
        {
            Title = "T",
            Type = "movie",
            Genres = new List<string> { "SCI-FI", " Drama " }
        });

        // У відповіді назви відсортовані абетково; існуючий жанр зберіг свою назву.
        Assert.Equal(new[] { "Drama", "Sci-Fi" }, result.Genres);
        Assert.Equal(2, await db.Genres.CountAsync());
        Assert.DoesNotContain(await db.Genres.ToListAsync(), g => g.Name == "SCI-FI");
    }

    [Fact]
    public async Task CreateAsync_NormalizesTags_DropsEmptyDuplicatesAndTrims()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var result = await service.CreateAsync(new CreateMediaDto
        {
            Title = "T",
            Type = "movie",
            Tags = new List<string> { " faves ", "", "   ", "FAVES", "watch later" }
        });

        Assert.Equal(new[] { "faves", "watch later" }, result.Tags);
        Assert.Equal(2, await db.Tags.CountAsync());
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MediaServiceCreateTests-{Guid.NewGuid()}")
            .Options);
}
