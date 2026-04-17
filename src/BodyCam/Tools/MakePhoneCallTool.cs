using System.ComponentModel;

namespace BodyCam.Tools;

public class MakePhoneCallArgs
{
    [Description("Contact name or phone number to call")]
    public string Contact { get; set; } = "";
}

public class MakePhoneCallTool : ToolBase<MakePhoneCallArgs>
{
    public override string Name => "make_phone_call";
    public override string Description =>
        "Make a phone call to a contact or number. " +
        "Use when the user asks to call someone.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-call_en_windows.ppn",
        Mode = WakeWordMode.FullSession
    };

    protected override async Task<ToolResult> ExecuteAsync(
        MakePhoneCallArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Contact))
            return ToolResult.Fail("Contact or phone number is required.");

        try
        {
            if (PhoneDialer.IsSupported)
            {
                PhoneDialer.Open(args.Contact);
                context.Log($"Calling: {args.Contact}");
                return ToolResult.Success(new { calling = args.Contact, status = "Call initiated" });
            }
            else
            {
                return ToolResult.Fail("Phone dialer is not supported on this device.");
            }
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to make call: {ex.Message}");
        }
    }
}
