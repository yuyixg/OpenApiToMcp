namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Top-level configuration for OpenApiToMcp multi-endpoint support.
/// Bound from the "OpenApiToMcp" section of appsettings.json.
/// </summary>
public class OpenApiToMcpConfig
{
    /// <summary>
    /// List of MCP endpoints, each backed by an OpenAPI specification.
    /// Each endpoint becomes an independent MCP service at /mcp/{Name}.
    /// </summary>
    public List<McpEndpointConfig> Endpoints { get; set; } = new();

    /// <summary>
    /// Route pattern for MCP endpoints. Use {endpoint} as placeholder for the endpoint name.
    /// Default: "/mcp/{endpoint}"
    /// </summary>
    public string RoutePattern { get; set; } = "/mcp/{endpoint}";
}
