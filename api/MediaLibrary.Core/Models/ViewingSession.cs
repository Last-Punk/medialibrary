namespace MediaLibrary.Core.Models.Entities;

public class ViewingSession
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;

    public DateTime Date { get; set; }
    public string? Note { get; set; }
    public string? Progress { get; set; }
}
