using System.ComponentModel.DataAnnotations;

namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Request body for importing an item from an external catalog with POST /api/search/import.
/// A duplicate ExternalId + ExternalSource pair returns 409 MEDIA_DUPLICATE (see MediaService).
/// Genres are optional. If omitted by the client, they are fetched from TMDB or RAWG
/// during the catalog import flow.
/// </summary>
public class ImportMediaDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalSource { get; set; } = string.Empty;

    [Required(ErrorMessage = "Title is required")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? OriginalTitle { get; set; }

    [Range(1900, 2100)]
    public int? Year { get; set; }

    // MediaService parses the string against the contract values: movie/series/game.
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    [MaxLength(2000)]
    public string? Overview { get; set; }

    public List<string> Genres { get; set; } = new();
}
