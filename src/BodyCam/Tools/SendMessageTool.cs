using System.ComponentModel;

namespace BodyCam.Tools;

public class SendMessageArgs
{
    [Description("Recipient name or phone number")]
    public string Recipient { get; set; } = "";

    [Description("Message text to send")]
    public string Message { get; set; } = "";

    [Description("Messaging app: sms or whatsapp")]
    public string App { get; set; } = "sms";
}

public class SendMessageTool : ToolBase<SendMessageArgs>
{
    public override string Name => "send_message";
    public override string Description =>
        "Send a text message via SMS or WhatsApp. " +
        "Use when the user asks to send a message to someone.";

    protected override async Task<ToolResult> ExecuteAsync(
        SendMessageArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Recipient))
            return ToolResult.Fail("Recipient is required.");
        if (string.IsNullOrWhiteSpace(args.Message))
            return ToolResult.Fail("Message text is required.");

        try
        {
            if (args.App.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri($"https://wa.me/{Uri.EscapeDataString(args.Recipient)}?text={Uri.EscapeDataString(args.Message)}");
                await Launcher.OpenAsync(uri);
                context.Log($"WhatsApp message to {args.Recipient}");
            }
            else
            {
                if (Sms.Default.IsComposeSupported)
                {
                    await Sms.Default.ComposeAsync(new SmsMessage(args.Message, new[] { args.Recipient }));
                    context.Log($"SMS to {args.Recipient}");
                }
                else
                {
                    return ToolResult.Fail("SMS is not supported on this device.");
                }
            }

            return ToolResult.Success(new
            {
                sent = true,
                recipient = args.Recipient,
                app = args.App,
                status = "Message sent"
            });
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to send message: {ex.Message}");
        }
    }
}
