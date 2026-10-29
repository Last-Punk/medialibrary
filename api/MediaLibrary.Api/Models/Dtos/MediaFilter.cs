namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Query-параметри GET /api/media. Усі фільтри опційні.
/// Пагінація за контрактом: default Limit = 20, max = 100.
/// </summary>
public class MediaFilter
{
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Genre { get; set; }
    public int? MinRating { get; set; }
    public string? Search { get; set; }
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 20;
}
