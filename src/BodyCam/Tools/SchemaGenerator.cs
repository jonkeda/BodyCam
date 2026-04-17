using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BodyCam.Tools;

public static class SchemaGenerator
{
    public static string Generate<T>() where T : class => Generate(typeof(T));

    public static string Generate(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var props = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in properties)
        {
            var name = GetJsonName(prop);
            var schema = new Dictionary<string, object>
            {
                ["type"] = GetJsonType(prop.PropertyType)
            };

            var desc = prop.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            if (desc is not null)
                schema["description"] = desc.Description;

            props[name] = schema;

            if (!IsNullable(prop))
                required.Add(name);
        }

        var root = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = props
        };

        if (required.Count > 0)
            root["required"] = required;

        return JsonSerializer.Serialize(root);
    }

    private static string GetJsonName(PropertyInfo prop)
    {
        var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        if (attr is not null) return attr.Name;

        var name = prop.Name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string GetJsonType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal)) return "number";
        if (underlying.IsArray || (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))) return "array";

        return "object";
    }

    private static bool IsNullable(PropertyInfo prop)
    {
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null) return true;

        var ctx = new NullabilityInfoContext();
        var info = ctx.Create(prop);
        return info.WriteState == NullabilityState.Nullable;
    }
}
