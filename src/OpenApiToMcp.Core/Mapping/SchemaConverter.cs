using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenApiToMcp.Core.Mapping;

/// <summary>
/// Converts OpenAPI Schema objects to JSON Schema 7 compatible JsonObject.
/// </summary>
public static class SchemaConverter
{
    /// <summary>
    /// Converts an OpenAPI schema to a JSON Schema 7 JsonNode.
    /// </summary>
    public static JsonNode? ToJsonSchema(IOpenApiSchema? schema)
    {
        if (schema == null) return null;

        var obj = new JsonObject();

        // Type mapping
        MapType(schema, obj);

        // Format
        if (!string.IsNullOrEmpty(schema.Format))
            obj["format"] = schema.Format;

        // Description
        if (!string.IsNullOrEmpty(schema.Description))
            obj["description"] = schema.Description;

        // Default — in 3.5.3 these are JsonNode? directly
        if (schema.Default != null)
            obj["default"] = JsonNode.Parse(schema.Default.ToJsonString());

        // Enum
        if (schema.Enum is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var val in schema.Enum)
                arr.Add(JsonNode.Parse(val.ToJsonString()));
            obj["enum"] = arr;
        }

        // Example — also JsonNode? directly
        if (schema.Example != null)
            obj["example"] = JsonNode.Parse(schema.Example.ToJsonString());

        // Numeric constraints
        if (schema.Minimum != null) obj["minimum"] = schema.Minimum;
        if (schema.Maximum != null) obj["maximum"] = schema.Maximum;
        if (schema.MultipleOf != null) obj["multipleOf"] = schema.MultipleOf;
        if (schema.ExclusiveMinimum != null) obj["exclusiveMinimum"] = schema.ExclusiveMinimum;
        if (schema.ExclusiveMaximum != null) obj["exclusiveMaximum"] = schema.ExclusiveMaximum;

        // String constraints
        if (schema.MinLength != null) obj["minLength"] = schema.MinLength;
        if (schema.MaxLength != null) obj["maxLength"] = schema.MaxLength;
        if (!string.IsNullOrEmpty(schema.Pattern)) obj["pattern"] = schema.Pattern;

        // Array constraints
        if (schema.MinItems != null) obj["minItems"] = schema.MinItems;
        if (schema.MaxItems != null) obj["maxItems"] = schema.MaxItems;
        if (schema.UniqueItems != null) obj["uniqueItems"] = schema.UniqueItems;

        // Object properties
        if (HasType(schema, JsonSchemaType.Object) && schema.Properties is { Count: > 0 })
        {
            var props = new JsonObject();
            foreach (var (name, propRef) in schema.Properties)
            {
                if (propRef != null)
                {
                    var converted = ToJsonSchema(propRef);
                    props[name] = converted != null ? JsonNode.Parse(converted.ToJsonString()) : new JsonObject();
                }
            }
            obj["properties"] = props;

            if (schema.Required is { Count: > 0 })
            {
                var req = new JsonArray();
                foreach (var r in schema.Required)
                    req.Add(r);
                obj["required"] = req;
            }
        }

        // AdditionalProperties
        if (schema.AdditionalProperties != null)
        {
            var converted = ToJsonSchema(schema.AdditionalProperties);
            if (converted != null)
                obj["additionalProperties"] = JsonNode.Parse(converted.ToJsonString());
        }

        // Array items
        if (HasType(schema, JsonSchemaType.Array) && schema.Items != null)
        {
            var converted = ToJsonSchema(schema.Items);
            if (converted != null)
                obj["items"] = JsonNode.Parse(converted.ToJsonString());
        }

        // Copy x- extensions
        if (schema.Extensions != null)
        {
            foreach (var ext in schema.Extensions)
            {
                if (ext.Key.StartsWith("x-") && ext.Value != null)
                    obj[ext.Key] = SerializeExtension(ext.Value);
            }
        }

        return obj;
    }

    /// <summary>
    /// Converts an OpenAPI parameter to a JSON Schema property entry and marks its location.
    /// </summary>
    public static (string Name, JsonNode Schema) ParameterToJsonSchema(OpenApiParameter param)
    {
        var propSchema = ToJsonSchema(param.Schema) as JsonObject ?? new JsonObject();

        if (!string.IsNullOrEmpty(param.Description))
            propSchema["description"] = param.Description;

        if (param.Deprecated)
            propSchema["deprecated"] = true;

        if (param.Example != null)
            propSchema["example"] = JsonNode.Parse(param.Example.ToJsonString());

        // Parameter location metadata
        propSchema["x-parameter-location"] = param.In?.ToString().ToLowerInvariant() ?? "query";

        // Copy x- extensions from parameter
        if (param.Extensions != null)
        {
            foreach (var ext in param.Extensions)
            {
                if (ext.Key.StartsWith("x-") && ext.Value != null)
                    propSchema[ext.Key] = SerializeExtension(ext.Value);
            }
        }

        return (param.Name, propSchema);
    }

    private static bool HasType(IOpenApiSchema schema, JsonSchemaType flag)
    {
        return (schema.Type & flag) == flag;
    }

    private static void MapType(IOpenApiSchema schema, JsonObject obj)
    {
        var type = schema.Type;
        var nullable = (type & JsonSchemaType.Null) != 0;

        string jsonType;
        if ((type & JsonSchemaType.Object) != 0) jsonType = "object";
        else if ((type & JsonSchemaType.Array) != 0) jsonType = "array";
        else if ((type & JsonSchemaType.Integer) != 0) jsonType = "integer";
        else if ((type & JsonSchemaType.Number) != 0) jsonType = "number";
        else if ((type & JsonSchemaType.Boolean) != 0) jsonType = "boolean";
        else if ((type & JsonSchemaType.String) != 0) jsonType = "string";
        else jsonType = "string";

        if (nullable)
            obj["type"] = JsonNode.Parse($"[\"{jsonType}\",\"null\"]");
        else
            obj["type"] = jsonType;
    }

    private static JsonNode? SerializeExtension(IOpenApiExtension ext)
    {
        try
        {
            using var ms = new MemoryStream();
            using var sw = new StreamWriter(ms, leaveOpen: true);
            var writer = new OpenApiJsonWriter(sw);
            ext.Write(writer, OpenApiSpecVersion.OpenApi3_0);
            sw.Flush();
            ms.Position = 0;
            return JsonNode.Parse(ms);
        }
        catch
        {
            return JsonValue.Create(ext.ToString());
        }
    }
}
