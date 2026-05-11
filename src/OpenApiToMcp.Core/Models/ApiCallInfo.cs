using Microsoft.OpenApi;

namespace OpenApiToMcp.Core.Models;

/// <summary>
/// Contains all details needed to construct and execute an HTTP request for an API operation.
/// </summary>
public record ApiCallInfo
{
    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE, PATCH, etc.).
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// URL path template with placeholders, e.g. /users/{userId}.
    /// </summary>
    public required string PathTemplate { get; init; }

    /// <summary>
    /// Base server URL for this call (no trailing slash).
    /// </summary>
    public required string ServerUrl { get; init; }

    /// <summary>
    /// All parameter definitions from the OpenAPI operation (path + operation level).
    /// </summary>
    public required IList<OpenApiParameter> Parameters { get; init; }

    /// <summary>
    /// Request body definition, if present.
    /// </summary>
    public OpenApiRequestBody? RequestBody { get; init; }

    /// <summary>
    /// Security requirements for this operation, or null if none.
    /// </summary>
    public IList<OpenApiSecurityRequirement>? SecurityRequirements { get; init; }

    /// <summary>
    /// Security scheme definitions from components.securitySchemes.
    /// </summary>
    public IDictionary<string, OpenApiSecurityScheme>? SecuritySchemes { get; init; }
}
