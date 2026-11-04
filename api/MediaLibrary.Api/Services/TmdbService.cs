using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Пошук фільмів і серіалів у TMDB (search/movie, search/tv).
/// Ключ і адреси беруться з конфігурації (секція Tmdb), у коді їх немає.
/// Будь-яка проблема зовнішнього сервісу (немає ключа, не-2xx, мережа, таймаут)
/// не валить запит: пишемо warning у лог і повертаємо порожній список —
/// агрегатор тоді віддасть лише те, що знайшов в інших джерелах.
/// </summary>
public class TmdbService
{
    public const string Source = "TMDB";

    private const string DefaultBaseUrl = "https://api.themoviedb.org/3/";
    private const string DefaultImageBaseUrl = "https://image.tmdb.org/t/p/w500";

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

    /// <summary>Фільми (search/movie).</summary>
    public Task<List<SearchResultDto>> SearchMoviesAsync(string query) =>
        SearchAsync("search/movie", "movie", query);

    /// <summary>Серіали (search/tv).</summary>
    public Task<List<SearchResultDto>> SearchSeriesAsync(string query) =>
        SearchAsync("search/tv", "series", query);

    private async Task<List<SearchResultDto>> SearchAsync(string path, string type, string query)
    {
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        if (_apiKey.Length == 0)
        {
            // Ключа немає — це штатний режим для розробки: працює лише локальна БД.
            _logger.LogWarning(
                "TMDB API key is not configured (Tmdb:ApiKey). Skipping external search for \"{Query}\".",
                query);
            return results;
        }

        // query обов'язково URL-кодуємо: пробіли, кирилиця та & інакше ламають запит.
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
                    // id у TMDB числовий, у контракті externalId — рядок.
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
            // Таймаут HttpClient або скасування запиту — не привід валити пошук.
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

        // TMDB віддає шлях із провідним слешем; склеюємо без подвійного "//".
        return _imageBaseUrl + path.TrimStart('/');
    }

    /// <summary>"2010-07-16" → 2010. Порожнє або сміття → null.</summary>
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

    // Формат відповіді TMDB — snake_case, тому імена полів задаємо явно.
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

        // Фільми віддають title/release_date, серіали — name/first_air_date.
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
