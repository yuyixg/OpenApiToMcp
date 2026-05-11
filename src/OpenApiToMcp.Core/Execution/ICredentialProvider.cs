using Microsoft.OpenApi;

namespace OpenApiToMcp.Core.Execution;

/// <summary>
/// Provides credentials for API authentication. Implementations can support
/// static credentials, OAuth2 token exchange, or custom token sources.
/// </summary>
public interface ICredentialProvider
{
    /// <summary>
    /// Gets a credential for the given security scheme.
    /// </summary>
    Task<CredentialResult> GetCredentialAsync(
        string schemeName,
        OpenApiSecurityScheme scheme,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a credential lookup.
/// </summary>
public record CredentialResult
{
    public required bool Success { get; init; }
    public string? Token { get; init; }
    public string? Error { get; init; }

    public static CredentialResult Ok(string token) => new() { Success = true, Token = token };
    public static CredentialResult Fail(string error) => new() { Success = false, Error = error };
}
