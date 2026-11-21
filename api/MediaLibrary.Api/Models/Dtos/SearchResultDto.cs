namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// A single search result (the contract SearchResult model).
/// Returned by GET /api/search.
/// </summary>
public class SearchResultDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalSource { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
}
