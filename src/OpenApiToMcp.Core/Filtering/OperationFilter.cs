using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Filtering;

/// <summary>
/// Filters OpenAPI operations based on include/exclude glob patterns.
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

        // Include patterns take priority
        if (options.IncludePatterns is { Count: > 0 })
        {
            return options.IncludePatterns.Any(pattern =>
                (!string.IsNullOrEmpty(operationId) && GlobMatcher.Matches(operationId, pattern)) ||
                GlobMatcher.Matches(urlPattern, pattern));
        }

        // Exclude patterns remove matching operations
        if (options.ExcludePatterns is { Count: > 0 })
        {
            return !options.ExcludePatterns.Any(pattern =>
                (!string.IsNullOrEmpty(operationId) && GlobMatcher.Matches(operationId, pattern)) ||
                GlobMatcher.Matches(urlPattern, pattern));
        }

        return true;
    }
}
