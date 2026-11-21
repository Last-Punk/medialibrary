using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Searches TMDB movies and TV series and retrieves genre metadata for imports.
/// A missing API key, non-success response, timeout, network error, or invalid JSON
/// is reported as an unavailable external catalog so the API can return the contract's
/// 503 EXTERNAL_API_UNAVAILABLE response.
/// </summary>
public class TmdbService
{
    public const string Source = "TMDB";

    private const string DefaultBaseUrl = "https://api.themoviedb.org/3/";
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

    /// <summary>Search movies using TMDB's search/movie endpoint.</summary>
    public Task<List<SearchResultDto>> SearchMoviesAsync(string query) =>
        SearchAsync("search/movie", "movie", query);

    /// <summary>Search TV series using TMDB's search/tv endpoint.</summary>
    public Task<List<SearchResultDto>> SearchSeriesAsync(string query) =>
        SearchAsync("search/tv", "series", query);

    /// <summary>Fetch the genre names for a TMDB movie or TV series.</summary>
    public async Task<List<string>> GetGenresAsync(string externalId, string type)
    {
        if (!int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("ExternalId must be a positive numeric TMDB ID", nameof(externalId));

        var path = type.Trim().ToLowerInvariant() switch
        {
            "movie" => "movie",
            "series" => "tv",
            _ => throw new ArgumentException("TMDB imports must have type movie or series", nameof(type))
        };

        if (_apiKey.Length == 0)
        {
            _logger.LogWarning("TMDB API key is not configured (Tmdb:ApiKey).");
            throw new ExternalApiUnavailableException(Source);
        }

        var url = $"{_baseUrl}{path}/{id}?api_key={Uri.EscapeDataString(_apiKey)}&language=en-US";
        var payload = await GetRequiredPayloadAsync<TmdbDetailsResponse>(url, $"{path}/{id}");
        if (payload.Genres is null)
            throw new ExternalApiUnavailableException(Source);

        return payload.Genres
            .Select(genre => TrimOrNull(genre.Name))
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<SearchResultDto>> SearchAsync(string path, string type, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResultDto>();

        if (_apiKey.Length == 0)
        {
            _logger.LogWarning("TMDB API key is not configured (Tmdb:ApiKey).");
            throw new ExternalApiUnavailableException(Source);
        }

        var url = $"{_baseUrl}{path}?api_key={Uri.EscapeDataString(_apiKey)}&query={Uri.EscapeDataString(query)}&language=en-US";
        var payload = await GetRequiredPayloadAsync<TmdbSearchResponse>(url, path);
        if (payload.Results is null)
            throw new ExternalApiUnavailableException(Source);

        var results = new List<SearchResultDto>();
        foreach (var item in payload.Results)
        {
            if (item.Id is null)
                continue;

            var title = FirstNonEmpty(item.Title, item.Name);
            if (title is null)
                continue;

            results.Add(new SearchResultDto
            {
                ExternalId = item.Id.Value.ToString(CultureInfo.InvariantCulture),
                ExternalSource = Source,
                Title = title,
                Year = ParseYear(FirstNonEmpty(item.ReleaseDate, item.FirstAirDate)),
                Type = type,
                PosterUrl = BuildPosterUrl(item.PosterPath),
                Overview = TrimOrNull(item.Overview)
            });
        }

        return results;
    }

    private async Task<TPayload> GetRequiredPayloadAsync<TPayload>(string url, string operation)
        where TPayload : class
    {
        try
        {
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "TMDB request for {Operation} failed with status {StatusCode}.",
                    operation,
                    (int)response.StatusCode);
                throw new ExternalApiUnavailableException(Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<TPayload>(stream, JsonOptions);
            if (payload is null)
            {
                _logger.LogWarning("TMDB response for {Operation} contained no usable payload.", operation);
                throw new ExternalApiUnavailableException(Source);
            }

            return payload;
        }
        catch (ExternalApiUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "TMDB request for {Operation} timed out or was cancelled.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TMDB request for {Operation} failed because of a network error.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "TMDB response for {Operation} could not be parsed.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
    }

    private string? BuildPosterUrl(string? posterPath)
    {
        var path = TrimOrNull(posterPath);
        return path is null ? null : _imageBaseUrl + path.TrimStart('/');
    }

    /// <summary>Convert a TMDB date such as "2010-07-16" to 2010.</summary>
    private static int? ParseYear(string? date)
    {
        var value = TrimOrNull(date);
        if (value is null || value.Length < 4)
            return null;

        return int.TryParse(value[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
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
        [JsonPropertyName("results")]
        public List<TmdbSearchItem>? Results { get; set; }
    }

    private sealed class TmdbSearchItem
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

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

    private sealed class TmdbDetailsResponse
    {
        [JsonPropertyName("genres")]
        public List<TmdbGenre>? Genres { get; set; }
    }

    private sealed class TmdbGenre
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
