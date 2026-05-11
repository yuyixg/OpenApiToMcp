using System.Text.Json.Nodes;

namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Describes an MCP tool derived from an OpenAPI operation.
/// </summary>
public record McpToolInfo
{
    /// <summary>
    /// Tool name — typically the OpenAPI operationId, or a custom x-mcp name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description of the tool.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema 7 object describing the tool's input parameters.
    /// </summary>
    public required JsonObject InputSchema { get; init; }

    /// <summary>
    /// Optional JSON Schema 7 object describing the expected output (from the 2xx response).
    /// </summary>
    public JsonObject? OutputSchema { get; init; }

    /// <summary>
    /// Optional annotations (e.g., x-openapi-path, x-openapi-method).
    /// </summary>
    public Dictionary<string, object?>? Annotations { get; init; }
}
