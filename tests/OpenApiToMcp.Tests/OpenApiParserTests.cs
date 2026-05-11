using Microsoft.OpenApi;
using OpenApiToMcp.Core.Models;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.Tests;

public class OpenApiParserTests
{
    private readonly IOpenApiSpecLoader _parser = TestHelpers.CreateParser();

    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task LoadSpecAsync_LoadsPetstoreJson()
    {
        var doc = await _parser.LoadSpecAsync(FixturePath("petstore-openapi.json"));

        Assert.NotNull(doc);
        Assert.Equal("Petstore API", doc.Info.Title);
        Assert.Equal("1.0.0", doc.Info.Version);
    }

    [Fact]
    public async Task LoadSpecAsync_ThrowsOnMissingFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _parser.LoadSpecAsync(FixturePath("nonexistent.json")));
    }

    [Fact]
    public void ValidateSpec_ThrowsOnMissingInfo()
    {
        var doc = new OpenApiDocument();

        Assert.Throws<InvalidOperationException>(() => _parser.ValidateSpec(doc));
    }

    [Fact]
    public void ValidateSpec_ThrowsOnEmptyPaths()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "1.0.0" },
            Paths = new OpenApiPaths()
        };

        Assert.Throws<InvalidOperationException>(() => _parser.ValidateSpec(doc));
    }

    [Fact]
    public void ValidateSpec_ThrowsOnNoOperations()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "1.0.0" },
            Paths = new OpenApiPaths
            {
                ["/empty"] = new OpenApiPathItem()
            }
        };

        Assert.Throws<InvalidOperationException>(() => _parser.ValidateSpec(doc));
    }

    [Fact]
    public async Task LoadAndProcessAsync_AppliesOverlays()
    {
        var options = new OpenApiToMcpOptions
        {
            SpecPath = FixturePath("petstore-openapi.json"),
            OverlayPaths = new List<string> { FixturePath("petstore-overlay.json") }
        };

        var doc = await _parser.LoadAndProcessAsync(options);

        Assert.Equal("Modified Petstore API", doc.Info.Title);
    }

    [Fact]
    public async Task LoadAndProcessAsync_ThrowsOnMissingServerUrl()
    {
        // Create a spec file without servers
        var tempFile = Path.GetTempFileName() + ".json";
        await File.WriteAllTextAsync(tempFile, """
        {
            "openapi": "3.0.0",
            "info": {"title": "No Servers", "version": "1.0.0"},
            "paths": {
                "/test": {
                    "get": {
                        "operationId": "test",
                        "responses": {"200": {"description": "ok"}}
                    }
                }
            }
        }
        """);

        var options = new OpenApiToMcpOptions { SpecPath = tempFile };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _parser.LoadAndProcessAsync(options));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadAndProcessAsync_WithTargetUrl_SkipsServerCheck()
    {
        var options = new OpenApiToMcpOptions
        {
            SpecPath = FixturePath("petstore-openapi.json"),
            TargetApiBaseUrl = "https://override.api.com"
        };

        var doc = await _parser.LoadAndProcessAsync(options);
        Assert.NotNull(doc);
    }
}
