using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Core.Models.Entities;
using MediaLibrary.Api.Services;

namespace MediaLibrary.Api.Tests;

/// <summary>
/// MediaService.ImportFromExternalAsync: мапінг зовнішнього результату (POST /api/search/import),
/// захист від дублікатів за ExternalId + ExternalSource (DuplicateException → 409 MEDIA_DUPLICATE)
/// та явна DataAnnotations-валідація DTO (ValidateDto).
/// </summary>
public class MediaServiceImportTests
{
    [Fact]
    public async Task ImportAsync_MapsDto_AndMarksRecordAsExternal()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = new ImportMediaDto
        {
            ExternalId = " 27205 ",
            ExternalSource = "TMDB",
            Title = "Inception",
            Year = 2010,
            Type = "movie",
            PosterUrl = "https://image.tmdb.org/t/p/w500/inception.jpg",
            Overview = "A thief who steals corporate secrets.",
            Genres = new List<string> { "Sci-Fi", "Thriller" }
        };

        var result = await service.ImportFromExternalAsync(dto);

        Assert.True(result.Id > 0);
        Assert.Equal("27205", result.ExternalId); // ідентифікатор нормалізується (trim)
        Assert.Equal("TMDB", result.ExternalSource);
        Assert.Equal("Inception", result.Title);
        Assert.Equal("Inception", result.OriginalTitle); // fallback на Title
        Assert.Equal(2010, result.Year);
        Assert.Equal("movie", result.Type);
        Assert.False(result.IsManualEntry);
        Assert.Null(result.Rating); // імпорт не проставляє оцінку
        Assert.Equal("plan", result.Status);
        Assert.Empty(result.Tags); // імпорт не приймає теги
        Assert.Equal(new[] { "Sci-Fi", "Thriller" }, result.Genres);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
    }

    [Fact]
    public async Task ImportAsync_DuplicateExternalIdAndSource_ThrowsDuplicateException()
    {
        using var db = CreateDb();
        var service = new MediaService(db);
        await service.ImportFromExternalAsync(ValidDto());

        var ex = await Assert.ThrowsAsync<DuplicateException>(
            () => service.ImportFromExternalAsync(ValidDto()));

        Assert.Equal("Media already exists", ex.Message);
        Assert.Equal(1, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_DuplicateCheck_IgnoresExternalSourceCase()
    {
        using var db = CreateDb();
        var service = new MediaService(db);
        await service.ImportFromExternalAsync(ValidDto(externalSource: "TMDB"));

        await Assert.ThrowsAsync<DuplicateException>(
            () => service.ImportFromExternalAsync(ValidDto(externalSource: "tmdb")));

        Assert.Equal(1, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_SameExternalIdFromAnotherSource_IsAllowed()
    {
        using var db = CreateDb();
        var service = new MediaService(db);
        await service.ImportFromExternalAsync(ValidDto(externalSource: "TMDB"));

        var second = await service.ImportFromExternalAsync(ValidDto(externalSource: "RAWG"));

        Assert.Equal("RAWG", second.ExternalSource);
        Assert.Equal(2, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_ManualCopyWithSameTitle_DoesNotBlockImport()
    {
        // Ручні записи мають ExternalId = null, тож не спрацьовують у перевірці дублікатів.
        using var db = CreateDb();
        var service = new MediaService(db);
        await service.CreateAsync(new CreateMediaDto { Title = "Manual copy", Type = "movie" });

        var imported = await service.ImportFromExternalAsync(ValidDto());

        Assert.Equal("27205", imported.ExternalId);
        Assert.Equal(2, await db.MediaItems.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportAsync_MissingExternalId_ThrowsArgumentException(string externalId)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = ValidDto();
        dto.ExternalId = externalId;

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportFromExternalAsync(dto));

        Assert.Equal("ExternalId is required", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImportAsync_MissingExternalSource_ThrowsArgumentException(string externalSource)
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = ValidDto();
        dto.ExternalSource = externalSource;

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportFromExternalAsync(dto));

        Assert.Equal("ExternalSource is required", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_MissingTitle_ThrowsArgumentException()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = ValidDto();
        dto.Title = "";

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportFromExternalAsync(dto));

        Assert.Equal("Title is required", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_YearOutOfRange_ThrowsArgumentException_FromDtoValidation()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = ValidDto();
        dto.Year = 1800;

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportFromExternalAsync(dto));

        Assert.Contains("Year", ex.Message);
        Assert.Equal(0, await db.MediaItems.CountAsync());
    }

    [Fact]
    public async Task ImportAsync_WithUnknownType_ThrowsArgumentException()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var dto = ValidDto();
        dto.Type = "documentary";

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportFromExternalAsync(dto));

        Assert.Contains("Invalid media type", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_ReusesGenresAcrossImports()
    {
        using var db = CreateDb();
        var service = new MediaService(db);

        var first = ValidDto(externalId: "27205");
        first.Genres = new List<string> { "Sci-Fi" };
        var second = ValidDto(externalId: "238");
        second.Genres = new List<string> { "sci-fi" };

        await service.ImportFromExternalAsync(first);
        var secondResult = await service.ImportFromExternalAsync(second);

        Assert.Equal(new[] { "Sci-Fi" }, secondResult.Genres);
        Assert.Equal(1, await db.Genres.CountAsync());
    }

    private static ImportMediaDto ValidDto(
        string externalId = "27205",
        string externalSource = "TMDB") =>
        new()
        {
            ExternalId = externalId,
            ExternalSource = externalSource,
            Title = "Inception",
            Year = 2010,
            Type = "movie"
        };

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"MediaServiceImportTests-{Guid.NewGuid()}")
            .Options);
}
