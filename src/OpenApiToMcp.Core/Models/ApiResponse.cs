namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Result of executing an API call.
/// </summary>
public record ApiResponse
{
    public required bool Success { get; init; }
    public required int StatusCode { get; init; }
    public string? Data { get; init; }
    public string? Error { get; init; }
}
