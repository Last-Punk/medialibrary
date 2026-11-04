namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Запит POST /api/media — створення запису вручну.
/// Обов'язкові: Title, Type. Якщо OriginalTitle не вказана — береться Title.
/// </summary>
public class CreateMediaDto
{
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public int? Year { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }
    public string Status { get; set; } = "plan";
    public string? Notes { get; set; }
}
