using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenApiToMcp.Core.Execution;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Overlays;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.Tests;

internal static class TestHelpers
{
    /// <summary>
    /// Creates a service provider with HttpClientFactory, logging, and all Core services for tests.
    /// </summary>
    public static ServiceProvider CreateTestServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddHttpClient("OpenApiToMcp");
        services.AddSingleton<IOverlayApplier, OverlayApplier>();
        services.AddSingleton<IOpenApiSpecLoader, OpenApiParser>();
        services.AddSingleton<IOperationMapper, OperationMapper>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a pre-configured OpenApiParser (as IOpenApiSpecLoader) for testing.
    /// </summary>
    public static IOpenApiSpecLoader CreateParser()
    {
        var sp = CreateTestServices();
        return sp.GetRequiredService<IOpenApiSpecLoader>();
    }

    /// <summary>
    /// Creates a pre-configured OperationMapper (as IOperationMapper) for testing.
    /// </summary>
    public static IOperationMapper CreateMapper()
    {
        var sp = CreateTestServices();
        return sp.GetRequiredService<IOperationMapper>();
    }
}
