using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Search TMDB movies and TV series (search/movie and search/tv).
/// The API key and URLs come from the Tmdb configuration section; no credentials are stored in code.
/// Any external-service problem (missing key, non-2xx response, network error, or timeout)
/// is logged as a warning and returns an empty list instead of failing the request;
/// the aggregator can still return results from other sources.
/// </summary>
public class TmdbService
{
    public const string Source = "TMDB";

    private const string DefaultBaseUrl = "https://api.themoviedb.org/3/";
    // EnsureTrailingSlash returns the fallback unchanged, so this trailing slash is required
    // to avoid concatenating poster paths without a separator when configuration is missing.
    private const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/w500/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<TmdbService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _imageBaseUrl;

    public TmdbService(HttpClient http, IConfiguration configuration, ILogger<TmdbService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = (configuration["Tmdb:ApiKey"] ?? string.Empty).Trim();
        _baseUrl = EnsureTrailingSlash(configuration["Tmdb:BaseUrl"], DefaultBaseUrl);
        _imageBaseUrl = EnsureTrailingSlash(configuration["Tmdb:ImageBaseUrl"], DefaultImageBaseUrl);
    }

    /// <summary>Search movies (search/movie).</summary>
    public Task<List<SearchResultDto>> SearchMoviesAsync(string query) =>
        SearchAsync("search/movie", "movie", query);

    /// <summary>Search TV series (search/tv).</summary>
    public Task<List<SearchResultDto>> SearchSeriesAsync(string query) =>
        SearchAsync("search/tv", "series", query);

    private async Task<List<SearchResultDto>> SearchAsync(string path, string type, string query)
    {
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        if (_apiKey.Length == 0)
        {
            // A missing key is a supported development mode: search only the local database.
            _logger.LogWarning(
                "TMDB API key is not configured (Tmdb:ApiKey). Skipping external search for \"{Query}\".",
                query);
            return results;
        }

        // URL-encode the query so spaces, Unicode, and ampersands do not break the request.
        var url = $"{_baseUrl}{path}?api_key={Uri.EscapeDataString(_apiKey)}&query={Uri.EscapeDataString(query)}&language=en-US";

        try
        {
            using var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "TMDB request to {Path} failed with status {StatusCode}. Returning empty result.",
                    path, (int)response.StatusCode);
                return results;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<TmdbSearchResponse>(stream, JsonOptions);

            if (payload?.Results is null)
                return results;

            foreach (var item in payload.Results)
            {
                if (item.Id is null)
                    continue;

                var title = FirstNonEmpty(item.Title, item.Name);
                if (title is null)
                    continue;

                results.Add(new SearchResultDto
                {
                    // TMDB IDs are numeric; the contract represents externalId as a string.
                    ExternalId = item.Id.Value.ToString(),
                    ExternalSource = Source,
                    Title = title,
                    Year = ParseYear(FirstNonEmpty(item.ReleaseDate, item.FirstAirDate)),
                    Type = type,
                    PosterUrl = BuildPosterUrl(item.PosterPath),
                    Overview = TrimOrNull(item.Overview)
                });
            }
        }
        catch (OperationCanceledException ex)
        {
            // An HttpClient timeout or request cancellation must not fail the overall search.
            _logger.LogWarning(ex, "TMDB request to {Path} was cancelled or timed out.", path);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TMDB request to {Path} failed: network error.", path);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "TMDB response for {Path} could not be parsed.", path);
        }

        return results;
    }

    private string? BuildPosterUrl(string? posterPath)
    {
        var path = TrimOrNull(posterPath);
        if (path is null)
            return null;

        // TMDB paths start with a slash; combine them without creating a double slash.
        return _imageBaseUrl + path.TrimStart('/');
    }

    /// <summary>Convert "2010-07-16" to 2010; return null for empty or invalid input.</summary>
    private static int? ParseYear(string? date)
    {
        var value = TrimOrNull(date);
        if (value is null || value.Length < 4)
            return null;

        return int.TryParse(value[..4], out var year) ? year : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = TrimOrNull(value);
            if (trimmed is not null)
                return trimmed;
        }

        return null;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string EnsureTrailingSlash(string? value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return fallback;

        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }

    // TMDB uses snake_case response fields, so map their names explicitly.
    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("page")]
        public int Page { get; set; }

        [JsonPropertyName("results")]
        public List<TmdbSearchItem>? Results { get; set; }

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    private sealed class TmdbSearchItem
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        // Movies use title/release_date; TV series use name/first_air_date.
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
    }
}
