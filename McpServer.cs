using Lichs.MCP.Core.Models;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Lichs.MCP.Core;

public class McpServer
{
    private readonly string _name;
    private readonly string _version;
    private readonly string _logPath;

    private readonly Dictionary<string, ToolDefinition> _tools = new();
    private readonly Dictionary<string, ResourceDefinition> _resources = new();
    private Func<List<ResourceInfo>>? _listResourcesHandler;
    private Func<string, ResourceContent?>? _readResourceHandler;

    // JSON Options 需與原專案一致
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public McpServer(string name, string version)
    {
        _name = name;
        _version = version;
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_debug_log.txt");
    }

    public void RegisterTool(string name, string description, object inputSchema, Func<JsonElement, string> handler)
    {
        _tools[name] = new ToolDefinition(name, description, inputSchema, handler);
    }

    public void RegisterResourceHandler(Func<List<ResourceInfo>> listHandler, Func<string, ResourceContent?> readHandler)
    {
        _listResourcesHandler = listHandler;
        _readResourceHandler = readHandler;
    }

    private bool _debugMode = false;

    public async Task RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        // Check for debug flag
        if (args.Contains("--debug"))
        {
            _debugMode = true;
        }

        // 1. CLI 模式 (直接測試用)
        if (args.Length > 0 && args[0] == "--test")
        {
            // 這裡可以保留擴充點，目前先簡單印出訊息
            Console.WriteLine($"[{_name}] CLI Test Mode Active. Tools: {_tools.Count}");
            return;
        }

        // 2. MCP Loop
        Log($"=== {_name} Started (Debug: {_debugMode}) ===");

        try
        {
            while (true)
            {
                string? line = await Console.In.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                Log($"[RECV]: {line}");

                JsonRpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
                }
                catch (Exception ex)
                {
                    Log($"[JSON ERROR]: {ex.Message}");
                    continue;
                }

                if (request == null) continue;

                object? result = null;
                JsonRpcError? error = null;

                try
                {
                    switch (request.Method)
                    {
                        case "initialize":
                            result = new
                            {
                                protocolVersion = "2024-11-05",
                                capabilities = new { tools = new { listChanged = true }, resources = new { listChanged = true, read = true } },
                                serverInfo = new { name = _name, version = _version }
                            };
                            break;

                        case "notifications/initialized":
                            // Handshake complete
                            break;

                        case "tools/list":
                            result = new { tools = _tools.Values.Select(t => new { name = t.Name, description = t.Description, inputSchema = t.InputSchema }).ToArray() };
                            break;

                        case "tools/call":
                            result = HandleToolCall(request.Params);
                            break;

                        case "resources/list":
                             result = HandleListResources();
                             break;
                        
                        case "resources/read":
                             result = HandleReadResource(request.Params);
                             break;

                        default:
                            if (request.Method.StartsWith("$/"))
                            {
                                // Ignore non-standard notifications
                            }
                            else
                            {
                                throw new McpException($"Method not found: {request.Method}", -32601);
                            }
                            break;
                    }
                }
                catch (McpException mcpEx)
                {
                    Log($"[MCP ERROR]: {mcpEx.Message}");
                    error = new JsonRpcError { Code = mcpEx.Code, Message = mcpEx.Message, Data = mcpEx.ErrorData };
                }
                catch (Exception ex)
                {
                    Log($"[FATAL ERROR]: {ex}");
                    error = new JsonRpcError { Code = -32603, Message = $"Internal Error: {ex.Message}" };
                }

                if (result != null || error != null)
                {
                    var response = new JsonRpcResponse { Id = request.Id, Result = result, Error = error };
                    SendResponse(response);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[CRITICAL LOOP ERROR]: {ex}");
        }
    }

    private object HandleToolCall(object? paramsObj)
    {
        if (paramsObj is not JsonElement paramsEl)
        {
            throw new McpException("Params must be a JSON object", -32602);
        }

        if (!paramsEl.TryGetProperty("name", out var nameProp))
        {
            throw new McpException("Missing 'name' in tool call params", -32602);
        }

        string name = nameProp.GetString() ?? "";

        if (!_tools.TryGetValue(name, out var tool))
        {
            throw new McpException($"Unknown tool: {name}", -32601);
        }

        if (!paramsEl.TryGetProperty("arguments", out var argsProp))
        {
            throw new McpException("Missing 'arguments' in tool call params", -32602);
        }

        string output = tool.Handler(argsProp);

        // Wrap format to match standardized MCP output (content array)
        return new { content = new[] { new { type = "text", text = output } } };
    }

    private object HandleListResources()
    {
        var resources = _listResourcesHandler?.Invoke() ?? new List<ResourceInfo>();
        return new { resources };
    }

    private object HandleReadResource(object? paramsObj)
    {
         if (paramsObj is not JsonElement paramsEl || !paramsEl.TryGetProperty("uri", out var uriProp))
        {
            throw new McpException("Missing 'uri' in resources/read params", -32602);
        }

        string uri = uriProp.GetString() ?? "";
        if (_readResourceHandler == null)
        {
             throw new McpException("No resource handler registered", -32601);
        }

        var content = _readResourceHandler.Invoke(uri);
        if (content == null)
        {
             throw new McpException($"Resource not found: {uri}", -32002);
        }

        return new { contents = new[] { content } };
    }

    private void SendResponse(JsonRpcResponse response)
    {
        string json = JsonSerializer.Serialize(response, _jsonOptions);
        Log($"[SEND]: {json}");
        Console.Write(json + "\n");
        Console.Out.Flush();
    }

    private void Log(string message)
    {
        if (!_debugMode) return;
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); }
        catch { }
    }

    private record ToolDefinition(string Name, string Description, object InputSchema, Func<JsonElement, string> Handler);
    
    // Resource Models (Internal use)
    private record ResourceDefinition(string Uri, string Name, string MimeType);

    public record ResourceInfo(string uri, string name, string? mimeType = null, string? description = null);
    public record ResourceContent(string uri, string? mimeType, string text);

    public void RegisterToolsFromAssembly(Assembly assembly)
    {
        var methods = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<Lichs.MCP.Core.Attributes.McpToolAttribute>() != null);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<Lichs.MCP.Core.Attributes.McpToolAttribute>()!;
            var schema = Lichs.MCP.Core.Utils.JsonSchemaGenerator.GenerateSchema(method);

            RegisterTool(attr.Name, attr.Description ?? "", schema, args =>
            {
                var parameters = method.GetParameters();
                var invokeArgs = new object?[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    var paramName = param.Name!; // Valid for Reflection

                    if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(paramName, out var propElement))
                    {
                        // Deserialization logic
                        try
                        {
                            invokeArgs[i] = JsonSerializer.Deserialize(propElement.GetRawText(), param.ParameterType, _jsonOptions);
                        }
                        catch (Exception ex)
                        {
                            throw new McpException($"Invalid parameter '{paramName}': {ex.Message}", -32602);
                        }
                    }
                    else if (param.HasDefaultValue)
                    {
                        invokeArgs[i] = param.DefaultValue;
                    }
                    else if (Nullable.GetUnderlyingType(param.ParameterType) != null || !param.ParameterType.IsValueType)
                    {
                        // Reference types or Nullables can be null if missing and not required logic handled earlier
                        // But if Required=true (default in Generator), we should have thrown earlier? 
                        // For dynamic binding, let's strictly check if McpParameter says required, or logic.
                        // For now, passing null is default behavior for missing non-default params.
                        invokeArgs[i] = null;
                    }
                    else
                    {
                        throw new McpException($"Missing required parameter '{paramName}'", -32602);
                    }
                }

                var result = method.Invoke(null, invokeArgs);

                // Handle return type
                if (result == null) return "null";
                if (method.ReturnType == typeof(string)) return (string)result;
                if (method.ReturnType == typeof(void) || method.ReturnType == typeof(Task)) return "success";

                // For Task<T>
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult(); // Sync wait for now, as Handler delegate is synchronous func currently
                    // If Generic Task<T>, get Result.
                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                    {
                        var resultProp = taskType.GetProperty("Result");
                        var taskResult = resultProp?.GetValue(task);
                        return taskResult is string s ? s : JsonSerializer.Serialize(taskResult, _jsonOptions);
                    }
                    return "success";
                }

                return JsonSerializer.Serialize(result, _jsonOptions);
            });
        }
    }
}
