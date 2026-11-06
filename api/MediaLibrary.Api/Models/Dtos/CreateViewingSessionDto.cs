namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Запит на створення сесії перегляду. Якщо Date не передано, сервер бере поточний UTC.
/// </summary>
public class CreateViewingSessionDto
{
    public DateTime? Date { get; set; }
    public string? Note { get; set; }
    public string? Progress { get; set; }
}
