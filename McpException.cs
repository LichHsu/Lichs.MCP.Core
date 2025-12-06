namespace Lichs.MCP.Core;

public class McpException : Exception
{
    public int Code { get; }
    public object? Data { get; }

    public McpException(string message, int code = -32603, object? data = null) 
        : base(message)
    {
        Code = code;
        Data = data;
    }
}
