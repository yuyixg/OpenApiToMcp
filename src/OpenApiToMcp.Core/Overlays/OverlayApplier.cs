using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace OpenApiToMcp.Core.Overlays;

/// <summary>
/// Applies OpenAPI Overlay Specification 1.0.0 actions to an OpenAPI document.
/// </summary>
public class OverlayApplier : IOverlayApplier
{
    public virtual OpenApiDocument Apply(OpenApiDocument target, JsonNode overlayDoc)
    {
        if (overlayDoc is not JsonObject root)
            throw new ArgumentException("Overlay must be a JSON object.");

        var overlayVersion = root["overlay"]?.GetValue<string>();
        if (string.IsNullOrEmpty(overlayVersion))
            throw new ArgumentException("Overlay document missing required 'overlay' version field.");

        var actions = root["actions"] as JsonArray;
        if (actions == null)
            return target;

        var targetJson = SerializeDocument(target);
        var workingDoc = JsonNode.Parse(targetJson.ToJsonString())!;

        foreach (var action in actions)
        {
            if (action is not JsonObject actionObj) continue;
            ApplyAction(workingDoc, actionObj);
        }

        return DeserializeDocument(workingDoc);
    }

    private void ApplyAction(JsonNode doc, JsonObject action)
    {
        var targetExpr = action["target"]?.GetValue<string>();
        if (string.IsNullOrEmpty(targetExpr))
            return;

        var remove = action["remove"]?.GetValue<bool>() ?? false;
        var update = action["update"];

        var matches = JsonPathQuery(doc, targetExpr);
        if (matches.Count == 0)
            return;

        foreach (var (parent, key) in matches)
        {
            if (remove)
            {
                RemoveFromParent(parent, key);
            }
            else if (update != null)
            {
                var targetNode = GetChild(parent, key);
                if (targetNode is JsonObject targetObj && update is JsonObject updateObj)
                    DeepMerge(targetObj, updateObj);
                else if (targetNode is JsonArray arr && update is JsonArray updateArr)
                {
                    foreach (var item in updateArr)
                        arr.Add(JsonNode.Parse(item!.ToJsonString()));
                }
                else
                    SetChild(parent, key, JsonNode.Parse(update.ToJsonString()));
            }
        }
    }

    private List<(JsonNode Parent, string Key)> JsonPathQuery(JsonNode root, string path)
    {
        var results = new List<(JsonNode, string)>();
        if (!path.StartsWith("$"))
            return results;

        var segments = ParseJsonPath(path);
        if (segments.Count == 0)
            return results;

        Traverse(root, segments, 0, results);
        return results;
    }

    private void Traverse(JsonNode current, List<string> segments, int index,
        List<(JsonNode Parent, string Key)> results)
    {
        if (index >= segments.Count) return;

        var segment = segments[index];
        bool isLast = index == segments.Count - 1;

        if (segment == "$")
        {
            Traverse(current, segments, index + 1, results);
            return;
        }

        if (segment == "*")
        {
            if (current is JsonObject obj)
            {
                foreach (var kvp in obj.ToList())
                {
                    if (isLast) results.Add((current, kvp.Key));
                    else if (kvp.Value != null) Traverse(kvp.Value, segments, index + 1, results);
                }
            }
            else if (current is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (isLast) results.Add((current, i.ToString()));
                    else if (arr[i] != null) Traverse(arr[i]!, segments, index + 1, results);
                }
            }
            return;
        }

        if (segment.StartsWith("?(@.") && current is JsonArray filterArr)
        {
            var filterMatch = System.Text.RegularExpressions.Regex.Match(
                segment, @"\?\(@\.(\w+)==['""]([^'""]+)['""]\)");
            if (filterMatch.Success)
            {
                var filterProp = filterMatch.Groups[1].Value;
                var filterValue = filterMatch.Groups[2].Value;

                for (int i = 0; i < filterArr.Count; i++)
                {
                    var item = filterArr[i];
                    if (item is JsonObject itemObj &&
                        itemObj[filterProp]?.GetValue<string>() == filterValue)
                    {
                        if (isLast) results.Add((current, i.ToString()));
                        else Traverse(item, segments, index + 1, results);
                    }
                }
                return;
            }
        }

        if (current is JsonObject namedObj && namedObj.ContainsKey(segment))
        {
            if (isLast) results.Add((current, segment));
            else if (namedObj[segment] != null) Traverse(namedObj[segment]!, segments, index + 1, results);
        }
        else if (current is JsonArray namedArr && int.TryParse(segment, out int arrIdx)
                 && arrIdx >= 0 && arrIdx < namedArr.Count)
        {
            if (isLast) results.Add((current, segment));
            else if (namedArr[arrIdx] != null) Traverse(namedArr[arrIdx]!, segments, index + 1, results);
        }
    }

    private List<string> ParseJsonPath(string path)
    {
        var segments = new List<string>();
        int i = 0;
        while (i < path.Length)
        {
            if (path[i] == '$') { segments.Add("$"); i++; }
            else if (path[i] == '.')
            {
                i++;
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i > start) segments.Add(path[start..i]);
            }
            else if (path[i] == '[')
            {
                i++;
                if (i < path.Length && (path[i] == '\'' || path[i] == '"'))
                {
                    char quote = path[i]; i++;
                    int start = i;
                    while (i < path.Length && path[i] != quote) i++;
                    segments.Add(path[start..i]);
                    if (i < path.Length) i++;
                }
                else
                {
                    int start = i;
                    while (i < path.Length && path[i] != ']') i++;
                    segments.Add(path[start..i]);
                }
                if (i < path.Length) i++;
            }
            else i++;
        }
        return segments;
    }

    private static JsonNode? GetChild(JsonNode parent, string key)
    {
        if (parent is JsonObject obj) return obj[key];
        if (parent is JsonArray arr && int.TryParse(key, out int idx) && idx >= 0 && idx < arr.Count)
            return arr[idx];
        return null;
    }

    private static void SetChild(JsonNode parent, string key, JsonNode? value)
    {
        if (parent is JsonObject obj) obj[key] = value;
        else if (parent is JsonArray arr && int.TryParse(key, out int idx) && idx >= 0 && idx < arr.Count)
            arr[idx] = value;
    }

    private static void RemoveFromParent(JsonNode parent, string key)
    {
        if (parent is JsonObject obj) obj.Remove(key);
        else if (parent is JsonArray arr && int.TryParse(key, out int idx) && idx >= 0 && idx < arr.Count)
            arr.RemoveAt(idx);
    }

    private static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var kvp in source)
        {
            if (target.ContainsKey(kvp.Key) &&
                target[kvp.Key] is JsonObject targetChild &&
                kvp.Value is JsonObject sourceChild)
                DeepMerge(targetChild, sourceChild);
            else
                target[kvp.Key] = JsonNode.Parse(kvp.Value!.ToJsonString());
        }
    }

    private static JsonObject SerializeDocument(OpenApiDocument doc)
    {
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, leaveOpen: true);
        var writer = new OpenApiJsonWriter(sw);
        doc.SerializeAsync(writer, OpenApiSpecVersion.OpenApi3_0).GetAwaiter().GetResult();
        sw.Flush();
        ms.Position = 0;
        return JsonNode.Parse(ms)!.AsObject();
    }

    private static OpenApiDocument DeserializeDocument(JsonNode json)
    {
        using var ms = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        var result = OpenApiDocument.LoadAsync(ms).GetAwaiter().GetResult();
        return result.Document!;
    }
}
