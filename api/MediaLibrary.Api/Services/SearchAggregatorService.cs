using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Search for GET /api/search: query the local database first, then external catalogs,
/// or return an empty list (the bot can then offer manual item creation).
/// The order is fixed (database → TMDB/RAWG), so a local item wins duplicate resolution
/// against its external catalog match.
/// </summary>
public class SearchAggregatorService
{
    // Maximum number of local items to return (the contract limit cap).
    private const int LocalLimit = 100;

    private readonly MediaService _media;
    private readonly TmdbService _tmdb;
    private readonly RawgService _rawg;
    private readonly ILogger<SearchAggregatorService> _logger;

    public SearchAggregatorService(
        MediaService media,
        TmdbService tmdb,
        RawgService rawg,
        ILogger<SearchAggregatorService> logger)
    {
        _media = media;
        _tmdb = tmdb;
        _rawg = rawg;
        _logger = logger;
    }

    /// <summary>
    /// type: null searches all sources; movie/series use TMDB; game uses RAWG.
    /// External API failures reduce the result set but do not fail the request.
    /// </summary>
    public async Task<List<SearchResultDto>> SearchAsync(string query, string? type)
    {
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        var search = query.Trim();
        var typeFilter = NormalizeType(type);

        // 1. Search the local database first; it is also the fallback when catalog keys are unset.
        results.AddRange(ToSearchResults(await SearchLocalAsync(search, typeFilter)));

        // 2. TMDB: movies for movie/no type, and TV series for series/no type.
        if (typeFilter is null or "movie")
            AddUnique(results, await SafeCall(() => _tmdb.SearchMoviesAsync(search), "TMDB movies"));

        if (typeFilter is null or "series")
            AddUnique(results, await SafeCall(() => _tmdb.SearchSeriesAsync(search), "TMDB series"));

        // 3. RAWG: games for game/no type.
        if (typeFilter is null or "game")
            AddUnique(results, await SafeCall(() => _rawg.SearchGamesAsync(search), "RAWG games"));

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
    /// Identify a manual item without ExternalId by ("Manual", its string ID),
    /// so it can appear in search without being confused with a TMDB/RAWG result.
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
    /// Deduplicate by ExternalSource + ExternalId, ignoring case.
    /// Keep the first match, allowing a local item to override a TMDB/RAWG result.
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
    /// External services log their own failures; this guard handles unexpected exceptions
    /// so search still returns 200 when a catalog provider is unavailable.
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
    /// Normalize "Movie " to "movie"; map empty input to null (search all sources).
    /// Do not swallow an invalid type; let the endpoint return 400 VALIDATION_ERROR.
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
