namespace MediaLibrary.Api.Models.Entities;

public class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<MediaItem> MediaItems { get; set; } = new();
}
