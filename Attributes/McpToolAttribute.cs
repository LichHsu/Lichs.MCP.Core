namespace Lichs.MCP.Core.Attributes;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string? Description { get; }

    public McpToolAttribute(string name, string? description = null)
    {
        Name = name;
        Description = description;
    }
}
