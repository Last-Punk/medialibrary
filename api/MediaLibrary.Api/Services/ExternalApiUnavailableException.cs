namespace MediaLibrary.Api.Services;

/// <summary>
/// Indicates that a required external catalog could not be queried. The API maps this to
/// the contract response 503 EXTERNAL_API_UNAVAILABLE without exposing provider details.
/// </summary>
public sealed class ExternalApiUnavailableException : Exception
{
    public ExternalApiUnavailableException(string provider, Exception? innerException = null)
        : base($"{provider} is unavailable.", innerException)
    {
        Provider = provider;
    }

    /// <summary>The catalog provider that failed (for server-side logging).</summary>
    public string Provider { get; }
}
