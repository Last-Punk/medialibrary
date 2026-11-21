using System.ComponentModel.DataAnnotations;

namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Request body for PUT /api/media/{id}; all fields are optional.
/// A null or omitted field is unchanged; an empty Genres or Tags list clears that collection.
/// </summary>
public class UpdateMediaDto
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? OriginalTitle { get; set; }

    [Range(1900, 2100)]
    public int? Year { get; set; }

    public string? Type { get; set; }
    public string? PosterUrl { get; set; }

    [MaxLength(2000)]
    public string? Overview { get; set; }

    [Range(1, 10, ErrorMessage = "Rating must be between 1 and 10")]
    public int? Rating { get; set; }

    public string? Status { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public List<string>? Genres { get; set; }
    public List<string>? Tags { get; set; }
}
