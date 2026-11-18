namespace MediaLibrary.Api.Models.Dtos;

/// <summary>
/// A result page in the contract format: { total, offset, limit, items }.
/// </summary>
public class PagedResult<T>
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public List<T> Items { get; set; } = new();
}
