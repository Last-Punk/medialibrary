namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Full media item returned by the API (the contract MediaItem model).
/// Type and Status are lowercase strings ("movie", "completed", ...) as required by the contract.
/// </summary>
public class MediaResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public int? Rating { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsManualEntry { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
