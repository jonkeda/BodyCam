using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BodyCam.UITestKit.Pages;

namespace BodyCam.UITests.Tests.MainPage;

[Collection("BodyCam")]
[Trait("Category", "RealHardware")]
[Trait("Page", "MainPage")]
public class EchoCanaryUiTests
{
    private static readonly TimeSpan AssistantTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SilentWindow = TimeSpan.FromSeconds(18);

    private readonly BodyCamFixture _fixture;
    private Pages.MainPage Page => _fixture.MainPage;

    public EchoCanaryUiTests(BodyCamFixture fixture)
    {
        _fixture = fixture;
        _fixture.NavigateToHome();
    }

    [SkippableFact]
    public void RealtimeSpeech_DoesNotReturnAssistantAudioAsUserTranscript()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("BODYCAM_RUN_ECHO_CANARY_UI") == "1",
            "BODYCAM_RUN_ECHO_CANARY_UI not set to 1");

        var runDirectory = CreateRunDirectory();
        CapturePageSource(runDirectory, "00-initial");

        Page.SpeakButton.Click();
        Page.ListeningButton.Click();

        var baselineUserCount = GetTranscriptTexts("TranscriptYouEntryLabel").Count;
        var baselineAssistantCount = GetTranscriptTexts("TranscriptAiEntryLabel").Count;

        Page.MessageEntry.Clear(timeoutMs: 10000);
        Page.MessageEntry.Enter(GetPrompt(), timeoutMs: 10000);
        Page.MessageEntry.Submit(timeoutMs: 10000);

        var userCountBeforeCanary = WaitForTranscriptCount(
            "TranscriptYouEntryLabel",
            baselineUserCount + 1,
            TimeSpan.FromSeconds(3));

        if (userCountBeforeCanary < baselineUserCount + 1)
        {
            Page.SendMessageButton.Click();
            userCountBeforeCanary = WaitForTranscriptCount(
                "TranscriptYouEntryLabel",
                baselineUserCount + 1,
                TimeSpan.FromSeconds(8));
        }

        CapturePageSource(runDirectory, "01-after-send");

        var assistantText = WaitForStableAssistantText(baselineAssistantCount, AssistantTimeout);
        CapturePageSource(runDirectory, "02-after-assistant");

        Assert.False(
            string.IsNullOrWhiteSpace(assistantText),
            $"Assistant canary phrase was not observed within {AssistantTimeout.TotalSeconds:N0}s. Artifacts: {runDirectory}");

        var canaryTokens = ExtractCanaryTokens(assistantText);
        Assert.NotEmpty(canaryTokens);

        Thread.Sleep(SilentWindow);

        var userTextsAfterCanary = GetTranscriptTexts("TranscriptYouEntryLabel")
            .Skip(userCountBeforeCanary)
            .Select(StripRolePrefix)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var assistantTextsAfterCanary = GetTranscriptTexts("TranscriptAiEntryLabel")
            .Skip(baselineAssistantCount + 1)
            .Select(StripRolePrefix)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var echoedUserTexts = userTextsAfterCanary
            .Where(text => ContainsCanaryTokens(text, canaryTokens))
            .ToArray();

        var summary = string.Join(Environment.NewLine, new[]
        {
            $"Prompt: {GetPrompt()}",
            $"Assistant canary: {assistantText}",
            $"Canary tokens: {string.Join(", ", canaryTokens)}",
            $"User transcripts after canary: {string.Join(" | ", userTextsAfterCanary)}",
            $"Assistant responses after canary: {string.Join(" | ", assistantTextsAfterCanary)}"
        });
        File.WriteAllText(Path.Combine(runDirectory, "summary.txt"), summary);
        CapturePageSource(runDirectory, "03-final");

        Assert.Empty(echoedUserTexts);
        Assert.Empty(assistantTextsAfterCanary);
    }

    private int WaitForTranscriptCount(string automationId, int minimumCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            count = GetTranscriptTexts(automationId).Count;
            if (count >= minimumCount)
                return count;

            Thread.Sleep(250);
        }

        return count;
    }

    private string WaitForStableAssistantText(int baselineAssistantCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var stableSince = DateTime.UtcNow;
        var lastText = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            var text = GetTranscriptTexts("TranscriptAiEntryLabel")
                .Skip(baselineAssistantCount)
                .Select(StripRolePrefix)
                .FirstOrDefault(IsUsableAssistantText);

            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, lastText, StringComparison.Ordinal))
            {
                lastText = text;
                stableSince = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(lastText) && DateTime.UtcNow - stableSince >= TimeSpan.FromSeconds(2))
                return lastText;

            Thread.Sleep(500);
        }

        return lastText;
    }

    private IReadOnlyList<string> GetTranscriptTexts(string automationId)
    {
        return _fixture.Context.FindElements(Locator.ByAutomationId(automationId))
            .Select(element => element.Text ?? element.GetAttribute("Name") ?? string.Empty)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private void CapturePageSource(string runDirectory, string name)
    {
        if (_fixture.Context.Driver is IDiagnosticDriver diagnostic)
        {
            File.WriteAllText(
                Path.Combine(runDirectory, $"{name}.txt"),
                diagnostic.GetPageSource());
        }

        _fixture.Context.SaveScreenshot(Path.Combine(runDirectory, $"{name}.png"));
    }

    private static bool IsUsableAssistantText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Contains("Switch to Active mode", StringComparison.OrdinalIgnoreCase))
            return false;

        return Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    private static string GetPrompt()
    {
        return Environment.GetEnvironmentVariable("BODYCAM_ECHO_CANARY_PROMPT")
               ?? "For an echo canary test, speak exactly two uncommon words of your choice, then stop. Do not include any explanation or question.";
    }

    private static string[] ExtractCanaryTokens(string assistantText)
    {
        var normalizedTokens = Normalize(StripRolePrefix(assistantText))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var filtered = normalizedTokens
            .Where(token => token.Length >= 3)
            .Where(token => !CommonWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return filtered.Length > 0
            ? filtered
            : normalizedTokens.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool ContainsCanaryTokens(string text, IReadOnlyCollection<string> canaryTokens)
    {
        var observedTokens = Normalize(StripRolePrefix(text))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (observedTokens.Count == 0)
            return false;

        var matched = canaryTokens.Count(observedTokens.Contains);
        var required = Math.Min(canaryTokens.Count, Math.Max(1, (int)Math.Ceiling(canaryTokens.Count * 0.6)));

        return matched >= required;
    }

    private static string StripRolePrefix(string text)
    {
        return Regex.Replace(text.Trim(), "^(AI|You|Scan):\\s*", string.Empty, RegexOptions.IgnoreCase);
    }

    private static string Normalize(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
    }

    private static string CreateRunDirectory()
    {
        var root = FindSolutionRoot();
        var runDirectory = Path.Combine(
            root,
            ".my",
            "testresults",
            "echo-canary",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runDirectory);
        return runDirectory;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0)
                return directory.FullName;

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "a",
        "an",
        "and",
        "are",
        "canary",
        "choice",
        "course",
        "do",
        "echo",
        "exactly",
        "explanation",
        "for",
        "here",
        "include",
        "is",
        "not",
        "of",
        "okay",
        "please",
        "question",
        "say",
        "speak",
        "stop",
        "sure",
        "test",
        "the",
        "then",
        "this",
        "to",
        "two",
        "uncommon",
        "words",
        "your"
    };
}
