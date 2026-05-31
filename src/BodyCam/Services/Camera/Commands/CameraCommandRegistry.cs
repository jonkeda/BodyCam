namespace BodyCam.Services.Camera.Commands;

public sealed class CameraCommandRegistry : ICameraCommandRegistry
{
    private readonly IReadOnlyDictionary<string, ICameraCommand> _byId;
    private readonly IReadOnlyDictionary<string, ICameraCommand> _byToolName;

    public CameraCommandRegistry(IEnumerable<ICameraCommand> commands)
    {
        Commands = commands.OrderBy(c => c.DisplayName).ToArray();
        _byId = Commands.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        _byToolName = Commands
            .Where(c => !string.IsNullOrWhiteSpace(c.ToolName))
            .ToDictionary(c => c.ToolName!, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ICameraCommand> Commands { get; }

    public bool TryGet(string id, out ICameraCommand command) =>
        _byId.TryGetValue(id, out command!);

    public bool TryGetTool(string toolName, out ICameraCommand command) =>
        _byToolName.TryGetValue(toolName, out command!);

    public ICameraCommand GetRequired(string id) =>
        TryGet(id, out var command)
            ? command
            : throw new InvalidOperationException($"Unknown camera command '{id}'.");
}
