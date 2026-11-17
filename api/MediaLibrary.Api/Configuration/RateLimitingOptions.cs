namespace MediaLibrary.Api.Configuration;

/// <summary>Configuration for the API-wide and authentication rate limits.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section containing rate-limit settings.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Gets or sets the number of requests allowed per user or IP globally.</summary>
    public int GlobalPermitLimit { get; set; } = 100;

    /// <summary>Gets or sets the global fixed-window duration, in seconds.</summary>
    public int GlobalWindowSeconds { get; set; } = 60;

    /// <summary>Gets or sets the global queue limit.</summary>
    public int GlobalQueueLimit { get; set; }

    /// <summary>Gets or sets the number of Telegram authentication attempts allowed per IP.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    /// <summary>Gets or sets the authentication fixed-window duration, in seconds.</summary>
    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Gets or sets the authentication queue limit.</summary>
    public int AuthQueueLimit { get; set; }
}
