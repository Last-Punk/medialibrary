namespace MediaLibrary.Api.Configuration;

/// <summary>Configuration used to issue and validate API access tokens.</summary>
public sealed class JwtOptions
{
    /// <summary>The configuration section containing JWT settings.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Gets or sets the expected token issuer.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected token audience.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Gets or sets the symmetric signing secret.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Gets or sets the token lifetime, in hours.</summary>
    public int ExpiresHours { get; set; } = 24;
}
