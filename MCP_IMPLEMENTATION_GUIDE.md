# C# 實作 MCP (Model Context Protocol) 框架指南

這份指南將解釋如何使用 C# 從頭建立一個完整的 MCP 伺服器框架。MCP 的核心概念是透過標準輸入/輸出 (Stdio) 進行 JSON-RPC 2.0 通訊，這使得它成為一種「無組件繼承」的擴充模式——主程式不需要引用擴充元件的 DLL，而是透過作業系統層級的 Process 進行互動。

## 1. 核心架構：Process 通訊 (Stdio)

傳統擴充元件通常需要繼承介面 (Interface) 並編譯成 DLL 讓主程式載入。MCP 則不同：
- **Host (主程式)**：啟動擴充元件的執行檔 (Process)。
- **Client (擴充元件)**：監聽 `Console.In`，回應到 `Console.Out`。
- **介面**：JSON-RPC 2.0 協議。

這種設計解耦了語言與依賴，C# 主程式可以呼叫 Python 或 Rust 寫的擴充，反之亦然。

```mermaid
graph TD
    Host[Host Application\n(AI Agent / IDE)]
    Client[MCP Server\n(Your C# Tool)]
    
    Host -- "stdin (JSON-RPC Request)" --> Client
    Client -- "stdout (JSON-RPC Response)" --> Host
    Client -- "stderr (Logs)" --> Host
```

## 2. 實作步驟

要實作一個完整的 MCP 框架，我們需要處理三個部分：
1. **傳輸層 (Transport)**：讀寫 Stdio。
2. **協議層 (Protocol)**：解析 JSON-RPC。
3. **調度層 (Dispatcher)**：路由請求到具體的 C# 方法。

### 步驟 1: 定義 JSON-RPC 模型

首先，我們需要定義標準的 JSON-RPC 2.0 資料結構。

```csharp
using System.Text.Json.Serialization;

namespace Lichs.MCP.Core.Models;

// 請求物件 { "jsonrpc": "2.0", "method": "...", "params": {...}, "id": 1 }
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("params")] public object? Params { get; set; }
    [JsonPropertyName("id")] public object? Id { get; set; }
}

// 回應物件
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
    [JsonPropertyName("id")] public object? Id { get; set; }
}

// 錯誤物件
public class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}
```

### 步驟 2: 建立伺服器迴圈 (The MCP Loop)

这是框架的心臟。我們需要持續監聽 `Console.In`，直到收到終止訊號。

**關鍵實作 (`McpServer.cs`):**

```csharp
public async Task RunAsync()
{
    // 設定編碼為 UTF8，避免中文亂碼
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    Console.InputEncoding = new System.Text.UTF8Encoding(false);

    while (true)
    {
        // 1. 等待主程式發送指令 (一行 JSON)
        string? line = await Console.In.ReadLineAsync();
        if (line == null) break; // 串流結束，關閉程式
        if (string.IsNullOrWhiteSpace(line)) continue;

        try 
        {
            // 2. 反序列化請求
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _jsonOptions);
            if (request == null) continue;

            // 3. 處理請求
            object? result = null;
            
            switch (request.Method)
            {
                case "initialize":
                    // MCP 握手協議：回報伺服器能力
                    result = new { 
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { listChanged = true } },
                        serverInfo = new { name = _serverName, version = "1.0" }
                    };
                    break;

                case "tools/list":
                    // 列出所有可用工具
                    result = new { tools = _registeredTools.Values };
                    break;

                case "tools/call":
                    // 執行具體工具邏輯
                    result = ExecuteTool(request.Params);
                    break;
            }

            // 4. 發送回應 (寫入 stdout)
            var response = new JsonRpcResponse { Id = request.Id, Result = result };
            string jsonResponse = JsonSerializer.Serialize(response, _jsonOptions);
            
            Console.Write(jsonResponse + "\n"); // 必須加上換行符號
            Console.Out.Flush(); // 確保立即發送
        }
        catch (Exception ex)
        {
            // 錯誤處理：發送 JSON-RPC Error
            SendError(request?.Id, -32603, ex.Message);
        }
    }
}
```

### 步驟 3: 自動化工具註冊 (Reflection)

為了達到「框架」的易用性，我們不希望每次手動註冊工具。我們可以使用 C# 的 Reflection 特性來掃描標籤 (Attribute)。

```csharp
// 1. 定義標籤
[AttributeUsage(AttributeTargets.Method)]
public class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public McpToolAttribute(string name, string description) 
    {
        Name = name; 
        Description = description;
    }
}

// 2. 在使用者程式碼中使用
public static class MyTools 
{
    [McpTool("add_numbers", "Adds two numbers")]
    public static int Add(int a, int b) => a + b;
}

// 3. 框架自動註冊邏輯
public void RegisterToolsFromAssembly(Assembly assembly)
{
    var methods = assembly.GetTypes()
        .SelectMany(t => t.GetMethods())
        .Where(m => m.GetCustomAttribute<McpToolAttribute>() != null);

    foreach (var method in methods)
    {
        var attr = method.GetCustomAttribute<McpToolAttribute>();
        // ... 將 method 封裝成 Delegate 並存入 Dictionary ...
    }
}
```

## 3. 為什麼這種模式強大？

這種模式不僅僅是 IPC (Inter-Process Communication)，它實現了一種**反轉控制 (Inversion of Control)**，但發生在進程級別。

1.  **隔離性**：擴充元件崩潰不會導致主程式 (AI Agent) 崩潰。
2.  **安全性**：可以限制該 Process 的權限。
3.  **多語言支援**：您可以混合使用 C# 處理業務邏輯、Python 處理數據分析、Node.js 處理網頁渲染，全部透過 MCP 協議統一溝通。

您的 `Lichs.MCP.Core` 專案已經實現了上述大部分邏輯，特別是在 `McpServer.cs` 中處理了核心的迴圈與異常捕捉，這是一個非常標準且穩健的實作方式。
