using System.ComponentModel.DataAnnotations;

namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Request body for manually creating a media item with POST /api/media.
/// Title and Type are required. If OriginalTitle is omitted, Title is used.
/// </summary>
public class CreateMediaDto
{
    [Required(ErrorMessage = "Title is required")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? OriginalTitle { get; set; }

    [Range(1900, 2100)]
    public int? Year { get; set; }

    // Keep Type as a string because the contract accepts movie/series/game, not numeric enum values.
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    [MaxLength(2000)]
    public string? Overview { get; set; }

    public List<string> Genres { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    [Range(1, 10, ErrorMessage = "Rating must be between 1 and 10")]
    public int? Rating { get; set; }

    public string Status { get; set; } = "plan";

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
