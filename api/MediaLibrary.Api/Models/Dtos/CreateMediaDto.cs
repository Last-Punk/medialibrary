using System.ComponentModel.DataAnnotations;

namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Запит POST /api/media — створення запису вручну.
/// Обов'язкові: Title, Type. Якщо OriginalTitle не вказана — береться Title.
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

    // Type лишається рядком: контракт приймає movie/series/game, а не числовий enum.
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
