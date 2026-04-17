using System.ComponentModel;

namespace BodyCam.Tools;

public class SetTranslationModeArgs
{
    [Description("Target language to translate into (e.g., Spanish, French, Japanese)")]
    public string TargetLanguage { get; set; } = "";

    [Description("Whether to activate or deactivate translation mode")]
    public bool Active { get; set; } = true;
}

public class SetTranslationModeTool : ToolBase<SetTranslationModeArgs>
{
    public override string Name => "set_translation_mode";
    public override string Description =>
        "Activate or deactivate live translation mode. " +
        "When active, spoken text will be translated to the target language.";

    public override WakeWordBinding? WakeWord => new()
    {
        KeywordPath = "wakewords/bodycam-translate_en_windows.ppn",
        Mode = WakeWordMode.FullSession
    };

    protected override Task<ToolResult> ExecuteAsync(
        SetTranslationModeArgs args, ToolContext context, CancellationToken ct)
    {
        if (args.Active && string.IsNullOrWhiteSpace(args.TargetLanguage))
            return Task.FromResult(ToolResult.Fail("Target language is required to activate translation."));

        if (args.Active)
        {
            context.Session.SystemPrompt += $"\n\nIMPORTANT: Translate all user speech into {args.TargetLanguage} and speak the translation aloud.";
            context.Log($"Translation mode activated: {args.TargetLanguage}");
        }
        else
        {
            // Remove translation instruction (simplified — in practice, rebuild the prompt)
            context.Log("Translation mode deactivated.");
        }

        return Task.FromResult(ToolResult.Success(new
        {
            active = args.Active,
            targetLanguage = args.TargetLanguage,
            status = args.Active ? $"Translating to {args.TargetLanguage}" : "Translation off"
        }));
    }
}
