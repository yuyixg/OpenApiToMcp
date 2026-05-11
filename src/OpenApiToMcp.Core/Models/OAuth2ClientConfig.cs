namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Configuration for an OAuth2 client credentials token exchange.
/// </summary>
public class OAuth2ClientConfig
{
    /// <summary>
    /// The token endpoint URL (e.g., https://auth.example.com/oauth/token).
    /// </summary>
    public string TokenEndpoint { get; set; } = "";

    /// <summary>
    /// The OAuth2 client ID.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The OAuth2 client secret.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Space-separated scopes to request. Optional.
    /// </summary>
    public string? Scopes { get; set; }

    /// <summary>
    /// The grant type. Default: "client_credentials".
    /// </summary>
    public string GrantType { get; set; } = "client_credentials";
}
