using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Execution;

/// <summary>
/// Executes HTTP API calls based on MCP tool invocation parameters.
/// </summary>
public class ApiClient
{
    public const string HttpClientName = "OpenApiToMcp";

    private readonly HttpClient _httpClient;
    private readonly OpenApiToMcpOptions _options;

    public ApiClient(HttpClient httpClient, OpenApiToMcpOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ApiResponse> ExecuteAsync(ApiCallInfo details, JsonElement? mcpInput, CancellationToken ct = default)
    {
        // 1. 基础验证
        if (string.IsNullOrWhiteSpace(details.Method))
            return new ApiResponse { Success = false, StatusCode = 400, Error = "Missing HTTP method" };
        if (string.IsNullOrWhiteSpace(details.PathTemplate))
            return new ApiResponse { Success = false, StatusCode = 400, Error = "Missing path template" };
        if (string.IsNullOrWhiteSpace(details.ServerUrl))
            return new ApiResponse { Success = false, StatusCode = 400, Error = "Missing server URL" };

        var input = ParseMcpInput(mcpInput);

        // 2. 解析参数并构建请求数据
        if (!TryBuildRequestData(details, input, out var urlPath, out var queryParams, out var headers, out var bodyData, out var errorResponse))
        {
            return errorResponse!;
        }

        // 3. 构建安全的 UriBuilder
        var baseUrl = details.ServerUrl.TrimEnd('/');
        var relativePath = urlPath.TrimStart('/');
        var uriBuilder = new UriBuilder($"{baseUrl}/{relativePath}");

        foreach (var qp in queryParams)
        {
            AppendQueryParameter(uriBuilder, qp.Key, qp.Value);
        }

        // 4. 初始化 HttpRequestMessage 并确保释放 (using)
        using var request = new HttpRequestMessage(new HttpMethod(details.Method), uriBuilder.Uri);

        // 5. 填充 Headers
        foreach (var h in headers)
            request.Headers.TryAddWithoutValidation(h.Key, h.Value);

        foreach (var h in _options.CustomHeaders)
            request.Headers.TryAddWithoutValidation(h.Key, h.Value);

        if (!_options.DisableXMcp)
            request.Headers.TryAddWithoutValidation("X-MCP", "1");

        // 6. 填充 Body
        if (bodyData.HasValue)
        {
            request.Content = new StringContent(bodyData.Value.Content, Encoding.UTF8, bodyData.Value.ContentType);
        }

        // 7. 应用安全策略（传入 uriBuilder 以便安全地修改 Query）
        ApplySecurity(request, uriBuilder, details.SecurityRequirements, details.SecuritySchemes);

        // 再次更新 RequestUri，因为 ApplySecurity 可能修改了 Query
        request.RequestUri = uriBuilder.Uri;

        // 8. 发送请求
        return await SendRequestAsync(request, ct);
    }

    private static Dictionary<string, JsonElement> ParseMcpInput(JsonElement? mcpInput)
    {
        return mcpInput?.ValueKind == JsonValueKind.Object
            ? mcpInput.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
            : new Dictionary<string, JsonElement>();
    }

    private bool TryBuildRequestData(
        ApiCallInfo details,
        Dictionary<string, JsonElement> input,
        out string urlPath,
        out List<KeyValuePair<string, string>> queryParams,
        out Dictionary<string, string> headers,
        out (string Content, string ContentType)? bodyData,
        out ApiResponse? errorResponse)
    {
        urlPath = details.PathTemplate;
        queryParams = new List<KeyValuePair<string, string>>();
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bodyData = null;
        errorResponse = null;

        foreach (var param in details.Parameters)
        {
            if (!input.TryGetValue(param.Name, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                if (param.Required)
                {
                    errorResponse = new ApiResponse { Success = false, StatusCode = 400, Error = $"Missing required parameter: {param.Name}" };
                    return false;
                }
                continue;
            }

            var strValue = JsonElementToString(value);

            switch (param.In)
            {
                case ParameterLocation.Path:
                    urlPath = urlPath.Replace($"{{{param.Name}}}", Uri.EscapeDataString(strValue));
                    break;
                case ParameterLocation.Query:
                    queryParams.Add(new KeyValuePair<string, string>(param.Name, strValue));
                    break;
                case ParameterLocation.Header:
                    headers[param.Name] = strValue;
                    break;
                case ParameterLocation.Cookie:
                    var cookieValue = $"{param.Name}={strValue}";
                    if (headers.TryGetValue("Cookie", out var existingCookie))
                        headers["Cookie"] = $"{existingCookie}; {cookieValue}";
                    else
                        headers["Cookie"] = cookieValue;
                    break;
            }
        }

        if (details.RequestBody != null && input.TryGetValue("requestBody", out var bodyValue)
            && bodyValue.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            bodyData = (bodyValue.GetRawText(), "application/json");
        }
        else if (details.RequestBody?.Required == true)
        {
            errorResponse = new ApiResponse { Success = false, StatusCode = 400, Error = "Missing required request body" };
            return false;
        }

        return true;
    }

    private void ApplySecurity(
        HttpRequestMessage request,
        UriBuilder uriBuilder,
        IList<OpenApiSecurityRequirement>? requirements,
        IDictionary<string, OpenApiSecurityScheme>? schemes)
    {
        if (requirements == null || requirements.Count == 0 || schemes == null)
            return;

        foreach (var requirement in requirements)
        {
            bool allSatisfied = true;

            foreach (var (schemeObj, _) in requirement)
            {
                var schemeName = schemeObj.Reference?.Id ?? schemeObj.Name; // 兼容引用的情况

                if (string.IsNullOrWhiteSpace(schemeName) || !schemes.TryGetValue(schemeName, out var scheme))
                {
                    allSatisfied = false;
                    break;
                }

                string? credential = null;
                _options.SecurityCredentials?.TryGetValue(schemeName, out credential);
                credential ??= _options.ApiKey;

                if (string.IsNullOrEmpty(credential))
                {
                    allSatisfied = false;
                    break;
                }

                switch (scheme.Type)
                {
                    case SecuritySchemeType.ApiKey:
                        switch (scheme.In)
                        {
                            case ParameterLocation.Header:
                                request.Headers.TryAddWithoutValidation(scheme.Name, credential);
                                break;
                            case ParameterLocation.Query:
                                AppendQueryParameter(uriBuilder, scheme.Name, credential);
                                break;
                            case ParameterLocation.Cookie:
                                request.Headers.TryAddWithoutValidation("Cookie", $"{scheme.Name}={credential}");
                                break;
                        }
                        break;

                    case SecuritySchemeType.Http:
                        if (string.Equals(scheme.Scheme, "basic", StringComparison.OrdinalIgnoreCase))
                        {
                            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                                credential.Contains(':') ? credential : $"{credential}:"));
                            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
                        }
                        else if (string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                        }
                        else
                        {
                            allSatisfied = false;
                        }
                        break;

                    case SecuritySchemeType.OAuth2:
                    case SecuritySchemeType.OpenIdConnect:
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                        break;

                    default:
                        allSatisfied = false;
                        break;
                }

                if (!allSatisfied) break;
            }

            if (allSatisfied) return; // 满足任意一组 security requirement 即可
        }
    }

    private async Task<ApiResponse> SendRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            // 修复：传入 CancellationToken
            var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return new ApiResponse { Success = true, StatusCode = (int)response.StatusCode, Data = responseBody };

            return new ApiResponse
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Error = $"API Error {(int)response.StatusCode}: {responseBody}",
                Data = responseBody
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiResponse { Success = false, StatusCode = 503, Error = $"Network error: {ex.Message}" };
        }
        catch (OperationCanceledException ex)
        {
            var reason = ct.IsCancellationRequested ? "Request cancelled by user" : "Request timeout";
            return new ApiResponse { Success = false, StatusCode = 408, Error = $"{reason}: {ex.Message}" };
        }
    }

    private static void AppendQueryParameter(UriBuilder builder, string key, string value)
    {
        var encodedKey = Uri.EscapeDataString(key);
        var encodedValue = Uri.EscapeDataString(value);
        var queryToAppend = $"{encodedKey}={encodedValue}";

        if (builder.Query.Length > 1)
            builder.Query = builder.Query.Substring(1) + "&" + queryToAppend;
        else
            builder.Query = queryToAppend;
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }
}