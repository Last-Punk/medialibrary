using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MediaLibrary.Core.Data;
using MediaLibrary.Api.Models.Dtos;
using MediaLibrary.Core.Models.Entities;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Дублікат за externalId + externalSource. Ендпоінти маплять у 409 MEDIA_DUPLICATE.
/// </summary>
public class DuplicateException : Exception
{
    public DuplicateException(string message) : base(message) { }
}

/// <summary>
/// CRUD медіатеки + мапінг Entity → DTO.
/// Невалідні вхідні дані — ArgumentException (ендпоінти маплять у 400 VALIDATION_ERROR).
/// </summary>
public class MediaService
{
    private readonly AppDbContext _db;

    public MediaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<MediaResponseDto>> GetFilteredAsync(MediaFilter filter)
    {
        var query = _db.MediaItems
            .Include(m => m.Genres)
            .Include(m => m.Tags)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            var type = ParseType(filter.Type);
            query = query.Where(m => m.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = ParseStatus(filter.Status);
            query = query.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Genre))
        {
            var genre = filter.Genre.Trim().ToLower();
            query = query.Where(m => m.Genres.Any(g => g.Name.ToLower() == genre));
        }

        if (filter.MinRating.HasValue)
        {
            query = query.Where(m => m.Rating >= filter.MinRating.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(m => m.Title.Contains(search) || m.OriginalTitle.Contains(search));
        }

        var total = await query.CountAsync();

        var offset = Math.Max(0, filter.Offset);
        var limit = filter.Limit <= 0 ? 20 : Math.Min(filter.Limit, 100);

        // Найновіші першими — так список очікує бот (/my).
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return new PagedResult<MediaResponseDto>
        {
            Total = total,
            Offset = offset,
            Limit = limit,
            Items = items.Select(ToDto).ToList()
        };
    }

    public async Task<MediaResponseDto?> GetByIdAsync(int id)
    {
        var item = await _db.MediaItems
            .Include(m => m.Genres)
            .Include(m => m.Tags)
            .FirstOrDefaultAsync(m => m.Id == id);

        return item is null ? null : ToDto(item);
    }

    public async Task<MediaResponseDto> CreateAsync(CreateMediaDto dto)
    {
        ValidateDto(dto);

        var title = (dto.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            throw new ArgumentException("Title is required");

        if (string.IsNullOrWhiteSpace(dto.Type))
            throw new ArgumentException("Type is required");

        ValidateRating(dto.Rating);

        var item = new MediaItem
        {
            Title = title,
            OriginalTitle = TrimOrDefault(dto.OriginalTitle, title),
            Year = dto.Year,
            Type = ParseType(dto.Type),
            PosterUrl = TrimOrNull(dto.PosterUrl),
            Overview = TrimOrNull(dto.Overview),
            ExternalId = null,
            ExternalSource = "Manual",
            Rating = dto.Rating,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? MediaStatus.Plan : ParseStatus(dto.Status),
            Notes = TrimOrNull(dto.Notes),
            IsManualEntry = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null,
            Genres = await GetOrCreateGenresAsync(dto.Genres),
            Tags = await GetOrCreateTagsAsync(dto.Tags)
        };

        _db.MediaItems.Add(item);
        await _db.SaveChangesAsync();

        return ToDto(item);
    }

    public async Task<MediaResponseDto?> UpdateAsync(int id, UpdateMediaDto dto)
    {
        ValidateDto(dto);

        var item = await _db.MediaItems
            .Include(m => m.Genres)
            .Include(m => m.Tags)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (item is null)
            return null;

        // null у DTO = поле не передане, лишаємо без змін.
        if (dto.Title is not null)
        {
            var title = dto.Title.Trim();
            if (title.Length == 0)
                throw new ArgumentException("Title is required");

            item.Title = title;
        }

        if (dto.OriginalTitle is not null)
            item.OriginalTitle = dto.OriginalTitle.Trim();

        if (dto.Year.HasValue)
            item.Year = dto.Year;

        if (dto.Type is not null)
            item.Type = ParseType(dto.Type);

        if (dto.PosterUrl is not null)
            item.PosterUrl = TrimOrNull(dto.PosterUrl);

        if (dto.Overview is not null)
            item.Overview = TrimOrNull(dto.Overview);

        if (dto.Rating.HasValue)
        {
            ValidateRating(dto.Rating);
            item.Rating = dto.Rating;
        }

        if (dto.Status is not null)
            item.Status = ParseStatus(dto.Status);

        if (dto.Notes is not null)
            item.Notes = TrimOrNull(dto.Notes);

        if (dto.Genres is not null)
        {
            item.Genres.Clear();
            item.Genres.AddRange(await GetOrCreateGenresAsync(dto.Genres));
        }

        if (dto.Tags is not null)
        {
            item.Tags.Clear();
            item.Tags.AddRange(await GetOrCreateTagsAsync(dto.Tags));
        }

        item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDto(item);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _db.MediaItems.FindAsync(id);
        if (item is null)
            return false;

        _db.MediaItems.Remove(item);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<MediaResponseDto> ImportFromExternalAsync(ImportMediaDto dto)
    {
        ValidateDto(dto);

        var externalId = (dto.ExternalId ?? string.Empty).Trim();
        var externalSource = (dto.ExternalSource ?? string.Empty).Trim();
        var title = (dto.Title ?? string.Empty).Trim();

        if (externalId.Length == 0)
            throw new ArgumentException("ExternalId is required");

        if (externalSource.Length == 0)
            throw new ArgumentException("ExternalSource is required");

        if (title.Length == 0)
            throw new ArgumentException("Title is required");

        if (string.IsNullOrWhiteSpace(dto.Type))
            throw new ArgumentException("Type is required");

        var sourceLower = externalSource.ToLower();
        var exists = await _db.MediaItems.AnyAsync(m =>
            m.ExternalId == externalId
            && m.ExternalSource != null
            && m.ExternalSource.ToLower() == sourceLower);

        if (exists)
            throw new DuplicateException("Media already exists");

        // Жанри з TMDB/RAWG окремим запитом підтягне етап інтеграції пошуку;
        // тут приймаємо лише ті, що клієнт передав у запиті.
        var item = new MediaItem
        {
            Title = title,
            OriginalTitle = TrimOrDefault(dto.OriginalTitle, title),
            Year = dto.Year,
            Type = ParseType(dto.Type),
            PosterUrl = TrimOrNull(dto.PosterUrl),
            Overview = TrimOrNull(dto.Overview),
            ExternalId = externalId,
            ExternalSource = externalSource,
            Rating = null,
            Status = MediaStatus.Plan,
            Notes = null,
            IsManualEntry = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null,
            Genres = await GetOrCreateGenresAsync(dto.Genres),
            Tags = new List<Tag>()
        };

        _db.MediaItems.Add(item);
        await _db.SaveChangesAsync();

        return ToDto(item);
    }

    public async Task<List<ViewingSessionDto>?> GetSessionsAsync(int mediaId)
    {
        var mediaExists = await _db.MediaItems
            .AsNoTracking()
            .AnyAsync(item => item.Id == mediaId);

        if (!mediaExists)
            return null;

        var sessions = await _db.ViewingSessions
            .AsNoTracking()
            .Where(session => session.MediaItemId == mediaId)
            .OrderByDescending(session => session.Date)
            .ThenByDescending(session => session.Id)
            .ToListAsync();

        return sessions.Select(ToDto).ToList();
    }

    public async Task<ViewingSessionDto?> AddSessionAsync(int mediaId, CreateViewingSessionDto dto)
    {
        var mediaExists = await _db.MediaItems
            .AsNoTracking()
            .AnyAsync(item => item.Id == mediaId);

        if (!mediaExists)
            return null;

        var session = new ViewingSession
        {
            MediaItemId = mediaId,
            Date = dto.Date.HasValue ? AsUtc(dto.Date.Value) : DateTime.UtcNow,
            Note = TrimOrNull(dto.Note),
            Progress = TrimOrNull(dto.Progress)
        };

        _db.ViewingSessions.Add(session);
        await _db.SaveChangesAsync();

        return ToDto(session);
    }

    private static ViewingSessionDto ToDto(ViewingSession session)
    {
        return new ViewingSessionDto
        {
            Id = session.Id,
            MediaItemId = session.MediaItemId,
            // SpecifyKind гарантує суфікс Z у JSON навіть після читання дати з SQLite.
            Date = AsUtc(session.Date),
            Note = session.Note,
            Progress = session.Progress
        };
    }

    private static MediaResponseDto ToDto(MediaItem item)
    {
        return new MediaResponseDto
        {
            Id = item.Id,
            Title = item.Title,
            OriginalTitle = item.OriginalTitle,
            Year = item.Year,
            // Enum → рядок у нижньому регістрі, як вимагає контракт ("movie", "completed").
            Type = item.Type.ToString().ToLowerInvariant(),
            PosterUrl = item.PosterUrl,
            Overview = item.Overview,
            ExternalId = item.ExternalId,
            ExternalSource = item.ExternalSource,
            Rating = item.Rating,
            Status = item.Status.ToString().ToLowerInvariant(),
            Notes = item.Notes,
            IsManualEntry = item.IsManualEntry,
            Genres = item.Genres.Select(g => g.Name).OrderBy(name => name).ToList(),
            Tags = item.Tags.Select(t => t.Name).OrderBy(name => name).ToList(),
            // У БД дати зберігаються в UTC. SpecifyKind — щоб JSON мав суфікс Z (ISO 8601 UTC з контракту).
            CreatedAt = AsUtc(item.CreatedAt),
            UpdatedAt = item.UpdatedAt.HasValue ? AsUtc(item.UpdatedAt.Value) : null
        };
    }

    private async Task<List<Genre>> GetOrCreateGenresAsync(IEnumerable<string>? names)
    {
        var normalized = NormalizeNames(names);
        var result = new List<Genre>();
        if (normalized.Count == 0)
            return result;

        // Довідник жанрів невеликий: порівнюємо в пам'яті без урахування регістру,
        // щоб коректно працювали й не-ASCII назви (SQL lower() у SQLite — лише ASCII).
        var existing = await _db.Genres.ToListAsync();

        foreach (var name in normalized)
        {
            var genre = existing.FirstOrDefault(g =>
                string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

            if (genre is null)
            {
                genre = new Genre { Name = name };
                _db.Genres.Add(genre);
                existing.Add(genre);
            }

            result.Add(genre);
        }

        return result;
    }

    private async Task<List<Tag>> GetOrCreateTagsAsync(IEnumerable<string>? names)
    {
        var normalized = NormalizeNames(names);
        var result = new List<Tag>();
        if (normalized.Count == 0)
            return result;

        var existing = await _db.Tags.ToListAsync();

        foreach (var name in normalized)
        {
            var tag = existing.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (tag is null)
            {
                tag = new Tag { Name = name };
                _db.Tags.Add(tag);
                existing.Add(tag);
            }

            result.Add(tag);
        }

        return result;
    }

    private static List<string> NormalizeNames(IEnumerable<string>? names)
    {
        if (names is null)
            return new List<string>();

        return names
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MediaType ParseType(string value)
    {
        if (Enum.TryParse<MediaType>(value.Trim(), ignoreCase: true, out var type)
            && Enum.IsDefined(type))
        {
            return type;
        }

        throw new ArgumentException(
            $"Invalid media type: '{value}'. Allowed: movie, series, game");
    }

    private static MediaStatus ParseStatus(string value)
    {
        if (Enum.TryParse<MediaStatus>(value.Trim(), ignoreCase: true, out var status)
            && Enum.IsDefined(status))
        {
            return status;
        }

        throw new ArgumentException(
            $"Invalid media status: '{value}'. Allowed: plan, watching, completed, dropped");
    }

    private static void ValidateDto<T>(T dto) where T : class
    {
        // Minimal APIs у .NET 9 самі не виконують DataAnnotations, тому запускаємо їх явно.
        var results = new List<ValidationResult>();
        var context = new ValidationContext(dto);

        if (!Validator.TryValidateObject(dto, context, results, validateAllProperties: true))
        {
            throw new ArgumentException(results[0].ErrorMessage ?? "Validation failed");
        }
    }

    private static void ValidateRating(int? rating)
    {
        if (rating.HasValue && (rating.Value < 1 || rating.Value > 10))
            throw new ArgumentException("Rating must be between 1 and 10");
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string TrimOrDefault(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? fallback : trimmed;
    }

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
