using System.ComponentModel.DataAnnotations;

namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Запит POST /api/search/import — імпорт знайденого в зовнішньому каталозі.
/// Дублікат за ExternalId + ExternalSource — 409 MEDIA_DUPLICATE (див. MediaService).
/// Genres опційні: якщо клієнт їх не передав, жанри підтягуватимуться з TMDB/RAWG
/// на етапі інтеграції пошуку.
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

    // Парсинг рядка у MediaService зберігає контрактні movie/series/game.
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    [MaxLength(2000)]
    public string? Overview { get; set; }

    public List<string> Genres { get; set; } = new();
}
