namespace MediaLibrary.Api.Models.Dtos;

/// <summary>Request body for Telegram Web App authentication.</summary>
public sealed class TelegramAuthRequest
{
    /// <summary>Gets or sets the raw Telegram Web App init-data query string.</summary>
    public string? InitData { get; set; }
}
