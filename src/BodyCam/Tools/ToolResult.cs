using System.Text.Json;

namespace BodyCam.Tools;

public class ToolResult
{
    public bool IsSuccess { get; private init; }
    public string? Error { get; private init; }
    public string Json { get; private init; } = "{}";

    public static ToolResult Success(object data)
    {
        return new ToolResult
        {
            IsSuccess = true,
            Json = JsonSerializer.Serialize(data)
        };
    }

    public static ToolResult Fail(string error)
    {
        return new ToolResult
        {
            IsSuccess = false,
            Error = error,
            Json = JsonSerializer.Serialize(new { error })
        };
    }
}
