using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Searches RAWG games and retrieves genre metadata for imports.
/// A missing API key, non-success response, timeout, network error, or invalid JSON
/// is reported as an unavailable external catalog so the API can return the contract's
/// 503 EXTERNAL_API_UNAVAILABLE response.
/// </summary>
public class RawgService
{
    public const string Source = "RAWG";

    private const string DefaultBaseUrl = "https://api.rawg.io/api/";
    private const int PageSize = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ILogger<RawgService> _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public RawgService(HttpClient http, IConfiguration configuration, ILogger<RawgService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = (configuration["Rawg:ApiKey"] ?? string.Empty).Trim();
        _baseUrl = EnsureTrailingSlash(configuration["Rawg:BaseUrl"], DefaultBaseUrl);
    }

    /// <summary>Search games by title.</summary>
    public async Task<List<SearchResultDto>> SearchGamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<SearchResultDto>();

        EnsureApiKey();

        var url = $"{_baseUrl}games?key={Uri.EscapeDataString(_apiKey)}"
                  + $"&search={Uri.EscapeDataString(query.Trim())}"
                  + $"&page_size={PageSize}";
        var payload = await GetRequiredPayloadAsync<RawgSearchResponse>(url, "game search");
        if (payload.Results is null)
            throw new ExternalApiUnavailableException(Source);

        var results = new List<SearchResultDto>();
        foreach (var item in payload.Results)
        {
            if (item.Id is null)
                continue;

            var title = TrimOrNull(item.Name);
            if (title is null)
                continue;

            results.Add(new SearchResultDto
            {
                ExternalId = item.Id.Value.ToString(CultureInfo.InvariantCulture),
                ExternalSource = Source,
                Title = title,
                Year = ParseYear(item.Released),
                Type = "game",
                PosterUrl = TrimOrNull(item.BackgroundImage),
                // RAWG's list response does not include a description.
                Overview = null
            });
        }

        return results;
    }

    /// <summary>Fetch the genre names for a RAWG game.</summary>
    public async Task<List<string>> GetGenresAsync(string externalId)
    {
        if (!int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("ExternalId must be a positive numeric RAWG ID", nameof(externalId));

        EnsureApiKey();

        var url = $"{_baseUrl}games/{id}?key={Uri.EscapeDataString(_apiKey)}";
        var payload = await GetRequiredPayloadAsync<RawgGameDetailsResponse>(url, $"game details/{id}");
        if (payload.Genres is null)
            throw new ExternalApiUnavailableException(Source);

        return payload.Genres
            .Select(genre => TrimOrNull(genre.Name))
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void EnsureApiKey()
    {
        if (_apiKey.Length > 0)
            return;

        _logger.LogWarning("RAWG API key is not configured (Rawg:ApiKey).");
        throw new ExternalApiUnavailableException(Source);
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
                    "RAWG request for {Operation} failed with status {StatusCode}.",
                    operation,
                    (int)response.StatusCode);
                throw new ExternalApiUnavailableException(Source);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<TPayload>(stream, JsonOptions);
            if (payload is null)
            {
                _logger.LogWarning("RAWG response for {Operation} contained no usable payload.", operation);
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
            _logger.LogWarning(ex, "RAWG request for {Operation} timed out or was cancelled.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RAWG request for {Operation} failed because of a network error.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RAWG response for {Operation} could not be parsed.", operation);
            throw new ExternalApiUnavailableException(Source, ex);
        }
    }

    /// <summary>Convert a RAWG release date such as "2015-05-19" to 2015.</summary>
    private static int? ParseYear(string? date)
    {
        var value = TrimOrNull(date);
        if (value is null || value.Length < 4)
            return null;

        return int.TryParse(value[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
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

    // RAWG uses snake_case response fields, so map their names explicitly.
    private sealed class RawgSearchResponse
    {
        [JsonPropertyName("results")]
        public List<RawgGame>? Results { get; set; }
    }

    private sealed class RawgGame
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("released")]
        public string? Released { get; set; }

        [JsonPropertyName("background_image")]
        public string? BackgroundImage { get; set; }
    }

    private sealed class RawgGameDetailsResponse
    {
        [JsonPropertyName("genres")]
        public List<RawgGenre>? Genres { get; set; }
    }

    private sealed class RawgGenre
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
