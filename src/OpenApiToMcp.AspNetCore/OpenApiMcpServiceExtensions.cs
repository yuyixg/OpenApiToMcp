using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenApiToMcp.Core.Execution;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Models;
using OpenApiToMcp.Core.Overlays;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.AspNetCore;

/// <summary>
/// Extension methods for configuring OpenApiToMcp services in DI.
/// </summary>
public static class OpenApiMcpServiceExtensions
{
    /// <summary>
    /// Registers OpenApiToMcp core services (parser, API client, mapper, overlay applier) with a single-endpoint configuration.
    /// All service types are registered as their interfaces, allowing custom implementations to replace them.
    /// </summary>
    public static IServiceCollection AddOpenApiMcp(
        this IServiceCollection services,
        Action<OpenApiToMcpOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IOverlayApplier, OverlayApplier>();
        services.AddSingleton<IOpenApiSpecLoader, OpenApiParser>();
        services.AddSingleton<IOperationMapper, OperationMapper>();
        services.AddHttpClient(ApiClient.HttpClientName);
        return services;
    }

    /// <summary>
    /// Registers OpenApiToMcp for multi-endpoint mode using appsettings.json configuration.
    /// Each endpoint in the config maps to an independent MCP service at a named route.
    /// </summary>
    public static IServiceCollection AddOpenApiMcp(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "OpenApiToMcp")
    {
        var config = configuration.GetSection(sectionName).Get<OpenApiToMcpConfig>()
            ?? new OpenApiToMcpConfig();

        return services.AddOpenApiMcp(config);
    }

    /// <summary>
    /// Registers OpenApiToMcp for multi-endpoint mode using a pre-built config object.
    /// Each endpoint in the config maps to an independent MCP service at a named route.
    /// </summary>
    public static IServiceCollection AddOpenApiMcp(
        this IServiceCollection services,
        OpenApiToMcpConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton<IOverlayApplier, OverlayApplier>();
        services.AddSingleton<IOpenApiSpecLoader, OpenApiParser>();
        services.AddSingleton<IOperationMapper, OperationMapper>();
        services.AddHttpClient(ApiClient.HttpClientName);
        return services;
    }
}
