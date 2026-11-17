namespace MediaLibrary.Core.Models.Entities;

/// <summary>
/// Represents a Telegram user and owns their private media library.
/// </summary>
public class User
{
    /// <summary>Gets or sets the internal identifier used by the API.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the unique Telegram user identifier.</summary>
    public long TelegramUserId { get; set; }

    /// <summary>Gets or sets the Telegram username, when one is available.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the Telegram first name, when one is available.</summary>
    public string? FirstName { get; set; }

    /// <summary>Gets or sets when the account was first created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the user last authenticated, in UTC.</summary>
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Gets the media items owned by this user.</summary>
    public List<MediaItem> MediaItems { get; set; } = new();
}
