# Lichs.MCP.Core

**Lichs.MCP.Core** 是 `Lichs.MCP` Workspace 的核心類別庫，提供建構 Model Context Protocol (MCP) 伺服器的標準化基礎建設。

它封裝了 JSON-RPC 通訊協定、錯誤處理、工具註冊與自動掃描機制，讓開發者能專注於工具邏輯的實作。

## 🌟 核心功能

### 1. McpServer
標準化的 MCP 伺服器實作，負責：
- 監聽與處理標準輸入/輸出 (StdIn/StdOut) 的 JSON-RPC 訊息。
- 支援 `initialize`, `tools/list`, `tools/call` 等標準 MCP 方法。
- 提供標準化的日誌記錄 (`mcp_debug_log.txt`)。
- 處理 CLI 參數 (如 `--test`) 與 MCP 模式的切換。

### 2. 自動掃描與註冊 ([McpTool])
支援基於 Reflection 的「自動掃描模式」，大幅簡化工具註冊流程。

- **`[McpTool]`**: 用於標記靜態方法為 MCP 工具。
- **`[McpParameter]`**: 用於描述參數用途、是否必填。
- **`RegisterToolsFromAssembly`**: 自動掃描 Assembly 中所有標記的方法並生成 JSON Schema。

### 3. 標準化錯誤處理
- **`McpException`**: 統一的例外類別，支援錯誤代碼 (Code) 與額外數據 (Data)。
- 自動捕獲並將 .NET Exception 轉換為標準 JSON-RPC Error 回應。

## 🚀 如何開發新工具

只需三個步驟即可新增一個 MCP 工具：

1. **建立靜態方法**
2. **加上 `[McpTool]` 屬性**
3. **加上 `[McpParameter]` 屬性 (選擇性)**

### 範例程式碼

```csharp
using Lichs.MCP.Core.Attributes;

public static class MyTools
{
    [McpTool("say_hello", "向使用者打招呼")]
    public static string SayHello(
        [McpParameter("使用者名稱")] string name,
        [McpParameter("是否大寫", false)] bool uppercase = false)
    {
        string message = $"Hello, {name}!";
        return uppercase ? message.ToUpper() : message;
    }
}
```

在 `Program.cs` 中註冊：

```csharp
var server = new McpServer("MyServer", "1.0.0");
server.RegisterToolsFromAssembly(Assembly.GetExecutingAssembly());
await server.RunAsync(args);
```

就是這麼簡單！Server 會自動生成如下的 Schema 並處理參數綁定：

```json
{
  "name": "say_hello",
  "description": "向使用者打招呼",
  "inputSchema": {
    "type": "object",
    "properties": {
      "name": { "type": "string", "description": "使用者名稱" },
      "uppercase": { "type": "boolean", "description": "是否大寫" }
    },
    "required": ["name"]
  }
}
```

## 📦 安裝與相依性

本專案相依於：
- `System.Text.Json`: 用於高效能 JSON 序列化。
- `System.Reflection`: 用於動態掃描與調用。

## 🛠️ 專案結構

- **Attributes/**: `McpToolAttribute`, `McpParameterAttribute`
- **Models/**: `JsonRpcRequest`, `JsonRpcResponse`, `JsonRpcError`
- **Utils/**: `JsonSchemaGenerator` (負責 Type -> JSON Schema 轉換)
- **McpServer.cs**: 伺服器核心邏輯
- **McpException.cs**: 錯誤處理模型