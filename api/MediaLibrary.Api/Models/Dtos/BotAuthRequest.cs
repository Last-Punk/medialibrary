namespace MediaLibrary.Api.Models.Dtos;

/// <summary>Telegram account identity asserted by the trusted bot server.</summary>
public sealed class BotAuthRequest
{
    /// <summary>Gets or sets the positive Telegram user ID from the verified Telegram update.</summary>
    public long TelegramUserId { get; set; }
}
