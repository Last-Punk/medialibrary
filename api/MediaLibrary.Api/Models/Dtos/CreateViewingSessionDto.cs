namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// Request body for creating a viewing session. If Date is omitted, the server uses the current UTC time.
/// </summary>
public class CreateViewingSessionDto
{
    public DateTime? Date { get; set; }
    public string? Note { get; set; }
    public string? Progress { get; set; }
}
