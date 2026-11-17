namespace MediaLibrary.Api.Configuration;

/// <summary>Configuration used to verify Telegram Web App initialization data.</summary>
public sealed class TelegramOptions
{
    /// <summary>The configuration section containing Telegram settings.</summary>
    public const string SectionName = "Telegram";

    /// <summary>Gets or sets the Telegram bot token used to verify init data.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum age of init data, in minutes.</summary>
    public int InitDataMaxAgeMinutes { get; set; } = 1440;
}
