namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Optional query parameters for GET /api/media.
/// Contract pagination defaults: Limit = 20, with a maximum of 100.
/// </summary>
public class MediaFilter
{
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Genre { get; set; }
    public int? MinRating { get; set; }
    public string? Search { get; set; }
    // Nullable so minimal-API [AsParameters] binding treats omitted query values as optional.
    public int? Offset { get; set; }
    public int? Limit { get; set; }
}
