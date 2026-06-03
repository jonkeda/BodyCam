using BodyCam.Services.Actions;
using BodyCam.Services.Transcript;
using FluentAssertions;

namespace BodyCam.Tests.Services.Transcript;

public sealed class InMemoryTranscriptStoreTests
{
    [Fact]
    public async Task AppendAsync_StoresRecordsBySession()
    {
        var store = new InMemoryTranscriptStore();

        await store.AppendAsync(new TranscriptRecord(
            "session-a",
            DateTimeOffset.UtcNow,
            "You",
            "Look",
            [new TranscriptMediaReference("image", "Captured frame", "image/jpeg", ByteLength: 42)],
            AssistiveActionIds.Look,
            ActionTriggerOrigin.ActionsDrawer,
            SourceProfileId: "phone",
            ProviderId: "openai",
            ModelId: "gpt-5.4"));
        await store.AppendAsync(new TranscriptRecord(
            "session-b",
            DateTimeOffset.UtcNow,
            "AI",
            "A desk is ahead.",
            []));

        var session = await store.GetSessionAsync("session-a");

        session.Should().ContainSingle();
        session[0].Text.Should().Be("Look");
        session[0].MediaReferences.Should().ContainSingle();
        session[0].ActionId.Should().Be(AssistiveActionIds.Look);
        session[0].TriggerOrigin.Should().Be(ActionTriggerOrigin.ActionsDrawer);
        (await store.ListSessionsAsync()).Should().BeEquivalentTo(["session-a", "session-b"]);
    }

    [Fact]
    public async Task ClearSessionAsync_RemovesOnlyRequestedSession()
    {
        var store = new InMemoryTranscriptStore();
        await store.AppendAsync(new TranscriptRecord("a", DateTimeOffset.UtcNow, "You", "one", []));
        await store.AppendAsync(new TranscriptRecord("b", DateTimeOffset.UtcNow, "You", "two", []));

        await store.ClearSessionAsync("a");

        (await store.GetSessionAsync("a")).Should().BeEmpty();
        (await store.GetSessionAsync("b")).Should().ContainSingle();
    }
}
