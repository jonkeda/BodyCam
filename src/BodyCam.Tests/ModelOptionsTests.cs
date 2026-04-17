using FluentAssertions;

namespace BodyCam.Tests;

public class ModelOptionsTests
{
    [Fact]
    public void RealtimeModels_ContainsDefault()
    {
        ModelOptions.RealtimeModels.Should().Contain(ModelOptions.DefaultRealtime);
    }

    [Fact]
    public void ChatModels_ContainsDefault()
    {
        ModelOptions.ChatModels.Should().Contain(ModelOptions.DefaultChat);
    }

    [Fact]
    public void VisionModels_ContainsDefault()
    {
        ModelOptions.VisionModels.Should().Contain(ModelOptions.DefaultVision);
    }

    [Fact]
    public void TranscriptionModels_ContainsDefault()
    {
        ModelOptions.TranscriptionModels.Should().Contain(ModelOptions.DefaultTranscription);
    }

    [Fact]
    public void Voices_ContainsDefault()
    {
        ModelOptions.Voices.Should().Contain(ModelOptions.DefaultVoice);
    }

    [Fact]
    public void AllArrays_AreNonEmpty()
    {
        ModelOptions.RealtimeModels.Should().NotBeEmpty();
        ModelOptions.ChatModels.Should().NotBeEmpty();
        ModelOptions.VisionModels.Should().NotBeEmpty();
        ModelOptions.TranscriptionModels.Should().NotBeEmpty();
        ModelOptions.Voices.Should().NotBeEmpty();
        ModelOptions.TurnDetectionModes.Should().NotBeEmpty();
        ModelOptions.NoiseReductionModes.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("gpt-realtime-1.5", "Realtime 1.5 (Premium)")]
    [InlineData("gpt-realtime-mini", "Realtime Mini (Budget)")]
    [InlineData("gpt-5.4", "GPT-5.4 (Flagship)")]
    [InlineData("gpt-5.4-mini", "GPT-5.4 Mini")]
    [InlineData("gpt-5.4-nano", "GPT-5.4 Nano (Cheapest)")]
    [InlineData("gpt-4o-mini-transcribe", "GPT-4o Mini Transcribe")]
    [InlineData("gpt-4o-transcribe", "GPT-4o Transcribe (Best)")]
    public void Label_ReturnsExpectedDisplayName(string modelId, string expected)
    {
        ModelOptions.Label(modelId).Should().Be(expected);
    }

    [Fact]
    public void Label_UnknownModel_ReturnsModelId()
    {
        ModelOptions.Label("unknown-model").Should().Be("unknown-model");
    }

    [Fact]
    public void DefaultRealtime_IsCorrectModelId()
    {
        // Validates the model ID fix from 8.3
        ModelOptions.DefaultRealtime.Should().Be("gpt-realtime-1.5");
    }

    [Fact]
    public void DefaultTranscription_IsCorrectModelId()
    {
        // Validates the model ID fix from 8.3
        ModelOptions.DefaultTranscription.Should().Be("gpt-4o-mini-transcribe");
    }

    [Fact]
    public void TurnDetectionModes_ContainsDefault()
    {
        ModelOptions.TurnDetectionModes.Should().Contain(ModelOptions.DefaultTurnDetection);
    }

    [Fact]
    public void NoiseReductionModes_ContainsDefault()
    {
        ModelOptions.NoiseReductionModes.Should().Contain(ModelOptions.DefaultNoiseReduction);
    }
}
