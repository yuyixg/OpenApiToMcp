using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Execution;

/// <summary>
/// Default credential provider with the following resolution order:
/// 1. OAuth2 client credentials (if OAuth2Clients is configured for the scheme) — with caching and auto-refresh
/// 2. Static SecurityCredentials dictionary
/// 3. Fallback ApiKey
/// </summary>
public class DefaultCredentialProvider : ICredentialProvider
{
    private readonly OpenApiToMcpOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DefaultCredentialProvider> _logger;

    // schemeName → cached token + expiry
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new();

    public DefaultCredentialProvider(
        OpenApiToMcpOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<DefaultCredentialProvider> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CredentialResult> GetCredentialAsync(
        string schemeName,
        OpenApiSecurityScheme scheme,
        CancellationToken ct = default)
    {
        // 1. OAuth2 client credentials
        if (_options.OAuth2Clients.TryGetValue(schemeName, out var oauth2Config))
        {
            return await GetOAuth2TokenAsync(schemeName, oauth2Config, ct);
        }

        // 2. Static SecurityCredentials
        if (_options.SecurityCredentials.TryGetValue(schemeName, out var cred) && !string.IsNullOrEmpty(cred))
        {
            return CredentialResult.Ok(cred);
        }

        // 3. Fallback ApiKey
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            return CredentialResult.Ok(_options.ApiKey);
        }

        return CredentialResult.Fail($"No credential found for scheme '{schemeName}'.");
    }

    private async Task<CredentialResult> GetOAuth2TokenAsync(
        string schemeName,
        OAuth2ClientConfig config,
        CancellationToken ct)
    {
        // Return cached token if still valid (with 30s buffer)
        if (_tokenCache.TryGetValue(schemeName, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return CredentialResult.Ok(cached.Token);
        }

        _logger.LogDebug("Requesting OAuth2 token for scheme '{SchemeName}' from {Endpoint}.", schemeName, config.TokenEndpoint);

        try
        {
            var httpClient = _httpClientFactory.CreateClient(ApiClient.HttpClientName);

            var formParams = new List<KeyValuePair<string, string>>
            {
                new("grant_type", config.GrantType),
                new("client_id", config.ClientId),
                new("client_secret", config.ClientSecret),
            };

            if (!string.IsNullOrEmpty(config.Scopes))
                formParams.Add(new("scope", config.Scopes));

            using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(formParams)
            };

            var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAuth2 token request failed for '{SchemeName}': {StatusCode} {Body}", schemeName, (int)response.StatusCode, body);
                return CredentialResult.Fail($"OAuth2 token request failed: {(int)response.StatusCode}");
            }

            var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            if (!root.TryGetProperty("access_token", out var accessTokenProp))
            {
                _logger.LogWarning("OAuth2 response for '{SchemeName}' missing 'access_token'.", schemeName);
                return CredentialResult.Fail("OAuth2 response missing 'access_token'.");
            }

            var token = accessTokenProp.GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var expiresInProp) ? expiresInProp.GetInt32() : 3600;

            _tokenCache[schemeName] = new CachedToken
            {
                Token = token,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
            };

            _logger.LogDebug("OAuth2 token obtained for '{SchemeName}', expires in {ExpiresIn}s.", schemeName, expiresIn);

            return CredentialResult.Ok(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth2 token exchange failed for '{SchemeName}'.", schemeName);
            return CredentialResult.Fail($"OAuth2 token exchange failed: {ex.Message}");
        }
    }

    private sealed class CachedToken
    {
        public required string Token { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
