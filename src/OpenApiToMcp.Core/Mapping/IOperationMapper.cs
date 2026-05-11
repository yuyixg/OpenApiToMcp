using Microsoft.OpenApi;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Mapping;

/// <summary>
/// Maps OpenAPI operations to MCP tool definitions.
/// </summary>
public interface IOperationMapper
{
    /// <summary>
    /// Maps all matching OpenAPI operations to MCP tool definitions.
    /// </summary>
    List<MappedOperation> MapToMcpTools(OpenApiDocument doc, OpenApiToMcpOptions options);
}
