using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Searches the owner's local library first, then the selected external catalogs.
/// A required catalog failure is propagated so the API can return 503
/// EXTERNAL_API_UNAVAILABLE, as specified in CONTRACT.md.
/// </summary>
public class SearchAggregatorService
{
    // Maximum number of local items to return (the contract limit cap).
    private const int LocalLimit = 100;

    private readonly MediaService _media;
    private readonly TmdbService _tmdb;
    private readonly RawgService _rawg;

    public SearchAggregatorService(MediaService media, TmdbService tmdb, RawgService rawg)
    {
        _media = media;
        _tmdb = tmdb;
        _rawg = rawg;
    }

    /// <summary>
    /// type: null searches all sources; movie/series use TMDB; game uses RAWG.
    /// If any selected external catalog is unavailable, the caller returns 503.
    /// </summary>
    public async Task<List<SearchResultDto>> SearchAsync(string query, string? type)
    {
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        var search = query.Trim();
        var typeFilter = NormalizeType(type);

        // 1. Search the local database first.
        results.AddRange(ToSearchResults(await SearchLocalAsync(search, typeFilter)));

        // 2. TMDB: movies for movie/no type, and TV series for series/no type.
        if (typeFilter is null or "movie")
            AddUnique(results, await _tmdb.SearchMoviesAsync(search));

        if (typeFilter is null or "series")
            AddUnique(results, await _tmdb.SearchSeriesAsync(search));

        // 3. RAWG: games for game/no type.
        if (typeFilter is null or "game")
            AddUnique(results, await _rawg.SearchGamesAsync(search));

        return results;
    }

    /// <summary>
    /// Fetch genre names for a catalog import. TMDB supports movies and series; RAWG supports games.
    /// Provider failures are propagated as ExternalApiUnavailableException for the contract 503 response.
    /// </summary>
    public Task<List<string>> FetchImportGenresAsync(ImportMediaDto dto)
    {
        var type = NormalizeType(dto.Type)!;
        var externalId = (dto.ExternalId ?? string.Empty).Trim();
        var externalSource = (dto.ExternalSource ?? string.Empty).Trim();

        if (string.Equals(externalSource, TmdbService.Source, StringComparison.OrdinalIgnoreCase))
        {
            if (type != "movie" && type != "series")
                throw new ArgumentException("TMDB imports must have type movie or series");

            return _tmdb.GetGenresAsync(externalId, type);
        }

        if (string.Equals(externalSource, RawgService.Source, StringComparison.OrdinalIgnoreCase))
        {
            if (type != "game")
                throw new ArgumentException("RAWG imports must have type game");

            return _rawg.GetGenresAsync(externalId);
        }

        throw new ArgumentException("ExternalSource must be TMDB or RAWG for catalog imports");
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
    /// Keep the first match, allowing a local item to override its external catalog match.
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
