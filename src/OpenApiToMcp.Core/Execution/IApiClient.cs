using System.Text.Json;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Execution;

/// <summary>
/// Executes HTTP API calls based on MCP tool invocation parameters.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Executes an API call with the given details and MCP input parameters.
    /// </summary>
    Task<ApiResponse> ExecuteAsync(ApiCallInfo details, JsonElement? mcpInput, CancellationToken ct = default);
}
