namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Запит PUT /api/media/{id} — усі поля опційні.
/// null (поле не передане) = залишити без змін; порожній список Genres/Tags = очистити.
/// </summary>
public class UpdateMediaDto
{
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public int? Year { get; set; }
    public string? Type { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public int? Rating { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
    public List<string>? Genres { get; set; }
    public List<string>? Tags { get; set; }
}
