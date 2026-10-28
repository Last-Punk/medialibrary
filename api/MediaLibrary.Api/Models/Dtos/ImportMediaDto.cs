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
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public int? Year { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public List<string> Genres { get; set; } = new();
}
