using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Пошук за контрактом GET /api/search: спочатку локальна БД, потім зовнішні каталоги.
/// Порядок фіксований — «БД → зовнішні», тому локальний запис завжди виграє дедуплікацію
/// у свого ж зовнішнього близнюка.
/// </summary>
public class SearchAggregatorService
{
    // Скільки локальних записів віддаємо максимум (стеля контракту для limit).
    private const int LocalLimit = 100;

    private readonly MediaService _media;
    private readonly TmdbService _tmdb;
    private readonly ILogger<SearchAggregatorService> _logger;

    public SearchAggregatorService(
        MediaService media,
        TmdbService tmdb,
        ILogger<SearchAggregatorService> logger)
    {
        _media = media;
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <summary>
    /// type: null — усі джерела; movie/series — TMDB; game — поки лише локальна БД.
    /// Помилки зовнішніх API не валять запит — вони лише зменшують результат.
    /// </summary>
    public async Task<List<SearchResultDto>> SearchAsync(string query, string? type)
    {
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        var search = query.Trim();
        var typeFilter = NormalizeType(type);

        // 1. Локальна БД першою — вона ж дає fallback, коли зовнішні ключі не налаштовані.
        results.AddRange(ToSearchResults(await SearchLocalAsync(search, typeFilter)));

        // 2. TMDB: фільми для movie/без типу, серіали для series/без типу.
        if (typeFilter is null or "movie")
            AddUnique(results, await SafeCall(() => _tmdb.SearchMoviesAsync(search), "TMDB movies"));

        if (typeFilter is null or "series")
            AddUnique(results, await SafeCall(() => _tmdb.SearchSeriesAsync(search), "TMDB series"));

        return results;
    }

    private async Task<List<MediaResponseDto>> SearchLocalAsync(string search, string? type)
    {
        var page = await _media.GetFilteredAsync(new MediaFilter
        {
            Search = search,
            Type = type,
            Offset = 0,
            Limit = LocalLimit
        });

        return page.Items;
    }

    /// <summary>
    /// MediaResponseDto → SearchResultDto.
    /// Ручний запис без ExternalId ідентифікуємо парою ("Manual", id у рядку),
    /// щоб його можна було показати в пошуку, але не сплутати з результатом TMDB/RAWG.
    /// </summary>
    private static IEnumerable<SearchResultDto> ToSearchResults(IEnumerable<MediaResponseDto> items)
    {
        foreach (var item in items)
        {
            var source = string.IsNullOrWhiteSpace(item.ExternalSource)
                ? "Manual"
                : item.ExternalSource.Trim();

            var externalId = string.IsNullOrWhiteSpace(item.ExternalId)
                ? item.Id.ToString()
                : item.ExternalId.Trim();

            yield return new SearchResultDto
            {
                ExternalId = externalId,
                ExternalSource = source,
                Title = item.Title,
                Year = item.Year,
                Type = item.Type,
                PosterUrl = item.PosterUrl,
                Overview = item.Overview
            };
        }
    }

    /// <summary>
    /// Дедуплікація за парою ExternalSource + ExternalId (без урахування регістру).
    /// Перший збіг лишається — тобто локальний запис перекриває результат TMDB/RAWG.
    /// </summary>
    private static void AddUnique(List<SearchResultDto> target, IEnumerable<SearchResultDto> candidates)
    {
        var seen = new HashSet<string>(
            target.Select(KeyOf),
            StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var key = KeyOf(candidate);
            if (key.Length == 0 || !seen.Add(key))
                continue;

            target.Add(candidate);
        }
    }

    private static string KeyOf(SearchResultDto result)
    {
        var source = (result.ExternalSource ?? string.Empty).Trim();
        var id = (result.ExternalId ?? string.Empty).Trim();

        if (source.Length == 0 || id.Length == 0)
            return string.Empty;

        return $"{source}|{id}";
    }

    /// <summary>
    /// Зовнішній сервіс сам логує свої проблеми, але на випадок непередбаченого
    /// винятку страхуємось і тут: пошук має віддати 200 навіть тоді, коли TMDB лежить.
    /// </summary>
    private async Task<List<SearchResultDto>> SafeCall(
        Func<Task<List<SearchResultDto>>> call,
        string sourceName)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search in {Source} failed; continuing with other sources.", sourceName);
            return new List<SearchResultDto>();
        }
    }

    /// <summary>
    /// "Movie " → "movie"; порожнє → null (шукаємо в усіх джерелах).
    /// Невалідний тип не ковтаємо — хай ендпоінт віддасть 400 VALIDATION_ERROR.
    /// </summary>
    private static string? NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var value = type.Trim().ToLowerInvariant();

        if (value is "movie" or "series" or "game")
            return value;

        throw new ArgumentException(
            $"Invalid media type: '{type}'. Allowed: movie, series, game");
    }
}
