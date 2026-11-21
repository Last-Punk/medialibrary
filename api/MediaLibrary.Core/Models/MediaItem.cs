namespace MediaLibrary.Core.Models.Entities;

public class MediaItem
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string OriginalTitle { get; set; } = string.Empty;
    public int? Year { get; set; }
    public MediaType Type { get; set; }
    public string? PosterUrl { get; set; }
    public string? Overview { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalSource { get; set; }
    public int? Rating { get; set; }
    public MediaStatus Status { get; set; }
    public string? Notes { get; set; }
    public bool IsManualEntry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<Genre> Genres { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
    public List<ViewingSession> Sessions { get; set; } = new();
}
