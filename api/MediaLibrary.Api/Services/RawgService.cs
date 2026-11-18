using System.Text.Json;
using System.Text.Json.Serialization;
using MediaLibrary.Api.Models.Dtos;

namespace MediaLibrary.Api.Services;

/// <summary>
/// Search RAWG games (GET games?search=...). The API key and base URL come from the Rawg configuration section.
/// As with TMDB, a missing key, non-2xx response, or network error is logged as a warning and returns an empty list,
/// so search returns fewer results instead of failing.
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
        var results = new List<SearchResultDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        if (_apiKey.Length == 0)
        {
            // A missing key is a supported development mode: search only the local database.
            _logger.LogWarning(
                "RAWG API key is not configured (Rawg:ApiKey). Skipping external search for \"{Query}\".",
                query);
            return results;
        }

        var url = $"{_baseUrl}games?key={Uri.EscapeDataString(_apiKey)}"
                  + $"&search={Uri.EscapeDataString(query.Trim())}"
                  + $"&page_size={PageSize}";

        try
        {
            using var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "RAWG request failed with status {StatusCode}. Returning empty result.",
                    (int)response.StatusCode);
                return results;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var payload = await JsonSerializer.DeserializeAsync<RawgSearchResponse>(stream, JsonOptions);

            if (payload?.Results is null)
                return results;

            foreach (var item in payload.Results)
            {
                if (item.Id is null)
                    continue;

                var title = TrimOrNull(item.Name);
                if (title is null)
                    continue;

                results.Add(new SearchResultDto
                {
                    ExternalId = item.Id.Value.ToString(),
                    ExternalSource = Source,
                    Title = title,
                    Year = ParseYear(item.Released),
                    Type = "game",
                    PosterUrl = TrimOrNull(item.BackgroundImage),
                    // RAWG does not include a description in this list response, so avoid a second request.
                    Overview = null
                });
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "RAWG request was cancelled or timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RAWG request failed: network error.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RAWG response could not be parsed.");
        }

        return results;
    }

    /// <summary>Convert "2015-05-19" to 2015; return null for empty or invalid input.</summary>
    private static int? ParseYear(string? date)
    {
        var value = TrimOrNull(date);
        if (value is null || value.Length < 4)
            return null;

        return int.TryParse(value[..4], out var year) ? year : null;
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
        [JsonPropertyName("count")]
        public int Count { get; set; }

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
}
