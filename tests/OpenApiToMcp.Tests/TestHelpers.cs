using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.Tests;

internal static class TestHelpers
{
    /// <summary>
    /// Creates a service provider with HttpClientFactory and logging for tests.
    /// </summary>
    public static ServiceProvider CreateTestServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddHttpClient("OpenApiToMcp");
        services.AddSingleton<OpenApiParser>();
        services.AddSingleton<OperationMapper>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a pre-configured OpenApiParser for testing.
    /// </summary>
    public static OpenApiParser CreateParser()
    {
        var sp = CreateTestServices();
        return sp.GetRequiredService<OpenApiParser>();
    }

    /// <summary>
    /// Creates a pre-configured OperationMapper for testing.
    /// </summary>
    public static OperationMapper CreateMapper()
    {
        var sp = CreateTestServices();
        return sp.GetRequiredService<OperationMapper>();
    }
}
