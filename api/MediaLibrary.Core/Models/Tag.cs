namespace MediaLibrary.Core.Models.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public List<MediaItem> MediaItems { get; set; } = new();
}
