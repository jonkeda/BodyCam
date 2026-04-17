using System.ComponentModel;

namespace BodyCam.Tools;

public class NavigateToArgs
{
    [Description("Destination address, place name, or coordinates")]
    public string Destination { get; set; } = "";

    [Description("Navigation mode: walking or driving")]
    public string Mode { get; set; } = "walking";
}

public class NavigateToTool : ToolBase<NavigateToArgs>
{
    public override string Name => "navigate_to";
    public override string Description =>
        "Start navigation to a destination. Opens the default maps app. " +
        "Use when the user asks for directions or how to get somewhere.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-navigate_en_windows.ppn",
        Mode = WakeWordMode.FullSession,
        InitialPrompt = "Where would you like to navigate to?"
    };

    protected override async Task<ToolResult> ExecuteAsync(
        NavigateToArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Destination))
            return ToolResult.Fail("Destination is required.");

        var dirFlag = args.Mode?.Equals("driving", StringComparison.OrdinalIgnoreCase) == true ? "d" : "w";
        var uri = $"https://maps.google.com/maps?daddr={Uri.EscapeDataString(args.Destination)}&dirflg={dirFlag}";

        try
        {
            await Launcher.OpenAsync(new Uri(uri));
            context.Log($"Navigation to {args.Destination} ({args.Mode})");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Could not open maps: {ex.Message}");
        }

        return ToolResult.Success(new
        {
            destination = args.Destination,
            mode = args.Mode,
            status = "Navigation started"
        });
    }
}
