using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Filtering;

/// <summary>
/// Filters OpenAPI operations based on whitelist/blacklist glob patterns.
/// </summary>
public static class OperationFilter
{
    /// <summary>
    /// Returns true if the operation should be included as an MCP tool.
    /// </summary>
    public static bool ShouldInclude(string? operationId, string path, string method, OpenApiToMcpOptions options)
    {
        var opId = operationId ?? $"{method.ToUpperInvariant()}:{path}";
        var urlPattern = $"{method.ToUpperInvariant()}:{path}";

        // Whitelist takes priority
        if (options.Whitelist is { Count: > 0 })
        {
            return options.Whitelist.Any(pattern =>
                (!string.IsNullOrEmpty(operationId) && GlobMatcher.Matches(operationId, pattern)) ||
                GlobMatcher.Matches(urlPattern, pattern));
        }

        // Blacklist excludes matching operations
        if (options.Blacklist is { Count: > 0 })
        {
            return !options.Blacklist.Any(pattern =>
                (!string.IsNullOrEmpty(operationId) && GlobMatcher.Matches(operationId, pattern)) ||
                GlobMatcher.Matches(urlPattern, pattern));
        }

        return true;
    }
}
