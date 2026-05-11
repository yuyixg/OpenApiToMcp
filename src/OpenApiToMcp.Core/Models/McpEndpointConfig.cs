namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Configuration for a single MCP endpoint backed by an OpenAPI spec.
/// Each endpoint maps to an independent MCP service at a named route.
/// </summary>
public class McpEndpointConfig
{
    /// <summary>
    /// Unique name for this endpoint. Used as the route segment: /mcp/{Name}.
    /// Must be URL-safe (letters, digits, hyphens, underscores).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Path or HTTP/HTTPS URL to the OpenAPI specification file (JSON or YAML).
    /// </summary>
    public string SpecPath { get; set; } = "";

    /// <summary>
    /// Paths or HTTP/HTTPS URLs to OpenAPI Overlay files to apply before processing.
    /// </summary>
    public List<string> OverlayPaths { get; set; } = new();

    /// <summary>
    /// Override the base server URL from the OpenAPI spec.
    /// If not set, the first server URL from the spec is used.
    /// </summary>
    public string? TargetApiBaseUrl { get; set; }

    /// <summary>
    /// Glob patterns for operationId or METHOD:/path to include.
    /// If set, only matching operations become MCP tools.
    /// </summary>
    public List<string>? Whitelist { get; set; }

    /// <summary>
    /// Glob patterns for operationId or METHOD:/path to exclude.
    /// Ignored if Whitelist is set.
    /// </summary>
    public List<string>? Blacklist { get; set; }

    /// <summary>
    /// Credentials keyed by security scheme name (e.g., "jwt": "your-token").
    /// </summary>
    public Dictionary<string, string> SecurityCredentials { get; set; } = new();

    /// <summary>
    /// Default API key. Used as fallback when SecurityCredentials doesn't contain the scheme.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom HTTP headers to include in all outgoing API requests for this endpoint.
    /// </summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = new();

    /// <summary>
    /// When true, the X-MCP: 1 header is not added to outgoing requests.
    /// </summary>
    public bool DisableXMcp { get; set; }
}
