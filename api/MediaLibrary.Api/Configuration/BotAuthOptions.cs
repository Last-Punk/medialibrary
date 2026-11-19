namespace MediaLibrary.Api.Configuration;

/// <summary>Credentials used only by the trusted server-side Telegram bot integration.</summary>
public sealed class BotAuthOptions
{
    /// <summary>The configuration section containing bot-to-API authentication settings.</summary>
    public const string SectionName = "BotAuth";

    /// <summary>Gets or sets the shared secret required by POST /api/auth/bot.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
