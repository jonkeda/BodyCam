using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.Input;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

/// <summary>
/// Tests for <see cref="HeyCyanButtonDefaults"/> — M33 Phase 4 Wave 2.
/// </summary>
public sealed class HeyCyanButtonDefaultsTests
{
    [Fact]
    public void SeedDefaults_PopulatesThreeMappings()
    {
        var map = new ActionMap();

        HeyCyanButtonDefaults.SeedDefaults(map);

        var action1 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap);
        var action2 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.DoubleTap);
        var action3 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.LongPress);

        action1.Should().Be(ButtonAction.ToggleConversation);
        action2.Should().Be(ButtonAction.Photo);
        action3.Should().Be(ButtonAction.EndSession);
    }

    [Fact]
    public void SeedDefaults_IsIdempotent()
    {
        var map = new ActionMap();

        HeyCyanButtonDefaults.SeedDefaults(map);
        HeyCyanButtonDefaults.SeedDefaults(map); // Second call should be no-op

        var action = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap);
        action.Should().Be(ButtonAction.ToggleConversation);
    }

    [Fact]
    public void SeedDefaults_PreservesUserOverrides()
    {
        var map = new ActionMap();
        
        // User has already set a custom mapping
        map.SetIfUnset("heycyan-glasses", "glasses-button", ButtonGesture.SingleTap, ButtonAction.Look);
        
        HeyCyanButtonDefaults.SeedDefaults(map);

        var action = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap);
        action.Should().Be(ButtonAction.Look, "user override should be preserved");
    }

    [Fact]
    public void SeedDefaults_OnlySetsUnsetMappings()
    {
        var map = new ActionMap();
        
        // Pre-populate DoubleTap with a different action
        map.SetIfUnset("heycyan-glasses", "glasses-button", ButtonGesture.DoubleTap, ButtonAction.Read);
        
        HeyCyanButtonDefaults.SeedDefaults(map);

        // DoubleTap should keep user's Read action
        var action1 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.DoubleTap);
        action1.Should().Be(ButtonAction.Read);

        // SingleTap and LongPress should get defaults
        var action2 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap);
        var action3 = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.LongPress);
        action2.Should().Be(ButtonAction.ToggleConversation);
        action3.Should().Be(ButtonAction.EndSession);
    }

    [Fact]
    public void SeedDefaults_DoesNotAffectOtherProviders()
    {
        var map = new ActionMap();
        
        // Set mapping for a different provider
        map.SetIfUnset("other-provider", "other-button", ButtonGesture.SingleTap, ButtonAction.Find);
        
        HeyCyanButtonDefaults.SeedDefaults(map);

        // Other provider mapping should be unchanged
        var otherAction = map.GetAction("other-provider:other-button", ButtonGesture.SingleTap);
        otherAction.Should().Be(ButtonAction.Find);

        // HeyCyan defaults should be set
        var heyCyanAction = map.GetAction("heycyan-glasses:glasses-button", ButtonGesture.SingleTap);
        heyCyanAction.Should().Be(ButtonAction.ToggleConversation);
    }

    [Fact]
    public void SupportedGestures_ReturnsThreeGestures()
    {
        var gestures = HeyCyanButtonDefaults.SupportedGestures;

        gestures.Should().HaveCount(3);
        gestures.Should().ContainInOrder(
            ButtonGesture.SingleTap,
            ButtonGesture.DoubleTap,
            ButtonGesture.LongPress);
    }

    [Fact]
    public void Constants_MatchProviderConstants()
    {
        HeyCyanButtonDefaults.ProviderId.Should().Be("heycyan-glasses");
        HeyCyanButtonDefaults.ButtonId.Should().Be("glasses-button");
        HeyCyanButtonDefaults.ProviderId.Should().Be(HeyCyanButtonProvider.ProviderIdConst);
        HeyCyanButtonDefaults.ButtonId.Should().Be(HeyCyanButtonProvider.ButtonIdConst);
    }
}
