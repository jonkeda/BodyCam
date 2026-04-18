using System.Text.Json;

namespace BodyCam.Tools;

public abstract class ToolBase<TArgs> : ITool where TArgs : class, new()
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual bool IsEnabled => true;
    public virtual WakeWordBinding? WakeWord => null;
    public string ParameterSchema => SchemaGenerator.Generate<TArgs>();

    public async Task<ToolResult> ExecuteAsync(JsonElement? arguments, ToolContext context, CancellationToken ct)
    {
        TArgs args;
        if (arguments is null || arguments.Value.ValueKind == JsonValueKind.Undefined)
        {
            args = new TArgs();
        }
        else
        {
            args = arguments.Value.Deserialize<TArgs>(JsonOptions) ?? new TArgs();
        }

        return await ExecuteAsync(args, context, ct);
    }

    protected abstract Task<ToolResult> ExecuteAsync(TArgs args, ToolContext context, CancellationToken ct);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
