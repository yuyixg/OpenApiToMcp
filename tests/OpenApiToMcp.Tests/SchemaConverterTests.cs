using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.Core.Mapping;

namespace OpenApiToMcp.Tests;

public class SchemaConverterTests
{
    [Fact]
    public void ToJsonSchema_Null_ReturnsNull()
    {
        Assert.Null(SchemaConverter.ToJsonSchema(null));
    }

    [Fact]
    public void ToJsonSchema_StringType_MapsCorrectly()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "A name"
        };

        var result = SchemaConverter.ToJsonSchema(schema);
        Assert.NotNull(result);
        Assert.Equal("string", result!["type"]!.GetValue<string>());
        Assert.Equal("A name", result["description"]!.GetValue<string>());
    }

    [Fact]
    public void ToJsonSchema_IntegerType_MapsCorrectly()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Format = "int32",
            Minimum = "0",
            Maximum = "100"
        };

        var result = SchemaConverter.ToJsonSchema(schema);
        Assert.NotNull(result);
        Assert.Equal("integer", result!["type"]!.GetValue<string>());
        Assert.Equal("int32", result["format"]!.GetValue<string>());
    }

    [Fact]
    public void ToJsonSchema_ArrayType_WithItems()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String }
        };

        var result = SchemaConverter.ToJsonSchema(schema);
        Assert.NotNull(result);
        Assert.Equal("array", result!["type"]!.GetValue<string>());
        Assert.NotNull(result["items"]);
        Assert.Equal("string", result["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ToJsonSchema_ObjectType_WithPropertiesAndRequired()
    {
        // Load from fixture to get properly initialized schema
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "petstore-openapi.json");
        var loadResult = OpenApiDocument.LoadAsync(fixturePath).GetAwaiter().GetResult();
        var doc = loadResult.Document!;

        // Get the NewPet schema which has properties and required
        var newPetSchema = doc.Components!.Schemas["NewPet"];

        var result = SchemaConverter.ToJsonSchema(newPetSchema);
        Assert.NotNull(result);
        Assert.Equal("object", result!["type"]!.GetValue<string>());

        var props = result["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("name"));

        var required = result["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains(required!, r => r!.GetValue<string>() == "name");
    }

    [Fact]
    public void ToJsonSchema_NullableType_MapsToArray()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null
        };

        var result = SchemaConverter.ToJsonSchema(schema);
        Assert.NotNull(result);
        var typeNode = result!["type"];
        Assert.NotNull(typeNode);
        if (typeNode is JsonArray arr)
        {
            Assert.Equal(2, arr.Count);
            Assert.Equal("string", arr[0]!.GetValue<string>());
            Assert.Equal("null", arr[1]!.GetValue<string>());
        }
    }

    [Fact]
    public void ParameterToJsonSchema_IncludesLocation()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "petstore-openapi.json");
        var loadResult = OpenApiDocument.LoadAsync(fixturePath).GetAwaiter().GetResult();
        var doc = loadResult.Document!;

        // Get the "limit" parameter from listPets
        var listPetsOp = doc.Paths["/pets"]!.Operations[HttpMethod.Get];
        var limitParam = (OpenApiParameter)listPetsOp.Parameters.First(p => p.Name == "limit");

        var (name, schema) = SchemaConverter.ParameterToJsonSchema(limitParam);
        Assert.Equal("limit", name);
        Assert.Equal("integer", schema["type"]!.GetValue<string>());
        Assert.Equal("query", schema["x-parameter-location"]!.GetValue<string>());
    }

    [Fact]
    public void ToJsonSchema_ObjectWithNestedProperties()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "petstore-openapi.json");
        var loadResult = OpenApiDocument.LoadAsync(fixturePath).GetAwaiter().GetResult();
        var doc = loadResult.Document!;

        var petSchema = doc.Components!.Schemas["Pet"];

        var result = SchemaConverter.ToJsonSchema(petSchema);
        Assert.NotNull(result);
        Assert.Equal("object", result!["type"]!.GetValue<string>());

        var props = result["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("id"));
        Assert.True(props.ContainsKey("name"));
        Assert.True(props.ContainsKey("tag"));
    }

    [Fact]
    public void ToJsonSchema_ArrayItemsObject()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "petstore-openapi.json");
        var loadResult = OpenApiDocument.LoadAsync(fixturePath).GetAwaiter().GetResult();
        var doc = loadResult.Document!;

        // Pets is an array of Pet
        var petsSchema = doc.Components!.Schemas["Pets"];

        var result = SchemaConverter.ToJsonSchema(petsSchema);
        Assert.NotNull(result);
        Assert.Equal("array", result!["type"]!.GetValue<string>());

        var items = result["items"] as JsonObject;
        Assert.NotNull(items);
        Assert.Equal("object", items!["type"]!.GetValue<string>());
    }
}
