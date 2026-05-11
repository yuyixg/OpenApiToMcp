namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Pairs an MCP tool definition with the API call details needed to execute it.
/// </summary>
public record MappedOperation
{
    /// <summary>
    /// The MCP tool definition exposed to clients.
    /// </summary>
    public required McpToolInfo ToolInfo { get; init; }

    /// <summary>
    /// Details for making the actual HTTP API call.
    /// </summary>
    public required ApiCallInfo CallInfo { get; init; }
}
