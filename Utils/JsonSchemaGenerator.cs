using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lichs.MCP.Core.Attributes;

namespace Lichs.MCP.Core.Utils;

public static class JsonSchemaGenerator
{
    public static object GenerateSchema(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var requiredList = new List<string>();
        var properties = new Dictionary<string, object>();

        foreach (var param in parameters)
        {
            var paramName = param.Name!; // C# parameters always have names
            var desc = param.GetCustomAttribute<McpParameterAttribute>()?.Description;
            var isRequired = param.GetCustomAttribute<McpParameterAttribute>()?.Required ?? !param.HasDefaultValue;

            if (isRequired)
            {
                requiredList.Add(paramName);
            }

            var propSchema = GenerateTypeSchema(param.ParameterType, desc);
            properties[paramName] = propSchema;
        }

        return new
        {
            type = "object",
            properties = properties,
            required = requiredList.Any() ? requiredList.ToArray() : null
        };
    }

    private static object GenerateTypeSchema(Type type, string? description)
    {
        var schema = new Dictionary<string, object>();

        if (description != null)
        {
            schema["description"] = description;
        }

        // Handle Nullable<T>
        if (Nullable.GetUnderlyingType(type) is Type underlyingType)
        {
            type = underlyingType;
             // JSON schema usually handles nullability via logic or multiple types, 
             // but for MCP simple tools, often we just document the base type.
             // Or we could do type = ["string", "null"] but standard MCP clients expect simple schemas.
             // We'll treat as base type but maybe not required (handled in parent).
        }

        if (type == typeof(string))
        {
            schema["type"] = "string";
        }
        else if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            schema["type"] = "integer";
        }
        else if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            schema["type"] = "number";
        }
        else if (type == typeof(bool))
        {
            schema["type"] = "boolean";
        }
        else if (type.IsEnum)
        {
            schema["type"] = "string";
            schema["enum"] = Enum.GetNames(type);
        }
        else if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
        {
            schema["type"] = "array";
            var itemType = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
            schema["items"] = GenerateTypeSchema(itemType, null);
        }
        else if (type == typeof(JsonElement) || type == typeof(object))
        {
             // Fallback for raw JSON or generic object
             schema["type"] = "object";
        }
        else
        {
            // Complex Object (DTO)
            // Recursively generate schema for properties
            schema["type"] = "object";
            var props = new Dictionary<string, object>();
            
            // Simple DTO usually just public properties
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pName = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name 
                            ?? (JsonNamingPolicy.CamelCase.ConvertName(p.Name));
                
                var pDesc = p.GetCustomAttribute<McpParameterAttribute>()?.Description; // Re-use parameter attribute on property? Or specific?
                // Or maybe just generic DescriptionAttribute? Let's assume Description if present.
                // For now, minimal support for nested DTO description.
                
                props[pName] = GenerateTypeSchema(p.PropertyType, pDesc);
            }
            schema["properties"] = props;
        }

        return schema;
    }
}
