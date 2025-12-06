namespace Lichs.MCP.Core.Attributes;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public class McpParameterAttribute : Attribute
{
    public string? Description { get; }
    public bool Required { get; }

    public McpParameterAttribute(string? description = null, bool required = true)
    {
        Description = description;
        Required = required;
    }
}
