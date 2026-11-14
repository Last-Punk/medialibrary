namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Сесія перегляду у відповідях API. Date серіалізується як ISO 8601 UTC.
/// </summary>
public class ViewingSessionDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
    public string? Progress { get; set; }
}
