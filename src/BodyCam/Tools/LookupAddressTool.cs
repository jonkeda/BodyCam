using System.ComponentModel;

namespace BodyCam.Tools;

public class LookupAddressArgs
{
    [Description("The place, business, or address to look up")]
    public string Query { get; set; } = "";
}

public class LookupAddressTool : ToolBase<LookupAddressArgs>
{
    public override string Name => "lookup_address";
    public override string Description =>
        "Look up an address, business, or place. " +
        "Use your knowledge to provide address information. " +
        "Use when the user asks for a location or address.";

    protected override Task<ToolResult> ExecuteAsync(
        LookupAddressArgs args, ToolContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Query))
            return Task.FromResult(ToolResult.Fail("Search query is required."));

        // The LLM has the knowledge to look up addresses — pass through
        return Task.FromResult(ToolResult.Success(new
        {
            query = args.Query,
            note = "Use your knowledge to provide the address for this query."
        }));
    }
}
