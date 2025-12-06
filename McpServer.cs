using System.Text.Json;
using Lichs.MCP.Core.Models;
using System.Text;

namespace Lichs.MCP.Core;

public class McpServer
{
    private readonly string _name;
    private readonly string _version;
    private readonly string _logPath;
    
    private readonly Dictionary<string, ToolDefinition> _tools = new();
    
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

    public async Task RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);

        // 1. CLI 模式 (直接測試用)
        if (args.Length > 0 && args[0] == "--test")
        {
            // 這裡可以保留擴充點，目前先簡單印出訊息
            Console.WriteLine($"[{_name}] CLI Test Mode Active. Tools: {_tools.Count}");
            return;
        }

        // 2. MCP Loop
        Log($"=== {_name} Started ===");
        
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
                                 capabilities = new { tools = new { listChanged = true } },
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
                            
                        default:
                            // 忽略未知方法或通知
                            break;
                    }
                }
                catch (McpException mcpEx)
                {
                    Log($"[MCP ERROR]: {mcpEx.Message}");
                    error = new JsonRpcError { Code = mcpEx.Code, Message = mcpEx.Message, Data = mcpEx.Data };
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

    private void SendResponse(JsonRpcResponse response)
    {
        string json = JsonSerializer.Serialize(response, _jsonOptions);
        Log($"[SEND]: {json}");
        Console.Write(json + "\n");
        Console.Out.Flush();
    }

    private void Log(string message)
    {
        try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); }
        catch { }
    }

    private record ToolDefinition(string Name, string Description, object InputSchema, Func<JsonElement, string> Handler);
}
