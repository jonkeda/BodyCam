using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses.HeyCyan;
using FluentAssertions;
using NSubstitute;

namespace BodyCam.Tests.Services;

public sealed class SourceProfileTests
{
    [Fact]
    public void PhoneSourceProfile_IsAlwaysAvailable()
    {
        var profile = new PhoneSourceProfile();

        profile.Id.Should().Be("phone");
        profile.IsAvailable.Should().BeTrue();
        profile.UnavailableReason.Should().BeNull();
        profile.FallbackPriority.Should().Be(10);
        profile.Order.Should().Be(10);
    }

    [Fact]
    public void LaptopSourceProfile_IsAlwaysAvailable()
    {
        var profile = new LaptopSourceProfile();

        profile.Id.Should().Be("laptop");
        profile.IsAvailable.Should().BeTrue();
        profile.UnavailableReason.Should().BeNull();
        profile.FallbackPriority.Should().Be(10);
        profile.Order.Should().Be(10);
    }

    [Fact]
    public void HeyCyanSourceProfile_IsAvailable_WhenConnected()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        session.State.Returns(HeyCyanState.Connected);
        var profile = new HeyCyanSourceProfile(session);

        profile.Id.Should().Be("heycyan-glasses");
        profile.IsAvailable.Should().BeTrue();
        profile.UnavailableReason.Should().BeNull();
        profile.FallbackPriority.Should().Be(100);
    }

    [Fact]
    public void HeyCyanSourceProfile_IsAvailable_WhenTransferMode()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        session.State.Returns(HeyCyanState.TransferMode);
        var profile = new HeyCyanSourceProfile(session);

        profile.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void HeyCyanSourceProfile_NotAvailable_WhenDisconnected()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        session.State.Returns(HeyCyanState.Disconnected);
        var profile = new HeyCyanSourceProfile(session);

        profile.IsAvailable.Should().BeFalse();
        profile.UnavailableReason.Should().Be("(not connected)");
    }

    [Theory]
    [InlineData(HeyCyanState.Scanning)]
    [InlineData(HeyCyanState.Connecting)]
    [InlineData(HeyCyanState.Disconnecting)]
    public void HeyCyanSourceProfile_NotAvailable_InTransientStates(HeyCyanState state)
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        session.State.Returns(state);
        var profile = new HeyCyanSourceProfile(session);

        profile.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void BluetoothSourceProfile_IsAvailable_WhenInputAvailable()
    {
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();
        btInput.IsAvailable.Returns(true);
        btOutput.IsAvailable.Returns(false);
        var profile = new BluetoothSourceProfile(btInput, btOutput);

        profile.Id.Should().Be("bluetooth");
        profile.IsAvailable.Should().BeTrue();
        profile.UnavailableReason.Should().BeNull();
    }

    [Fact]
    public void BluetoothSourceProfile_IsAvailable_WhenOutputAvailable()
    {
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();
        btInput.IsAvailable.Returns(false);
        btOutput.IsAvailable.Returns(true);
        var profile = new BluetoothSourceProfile(btInput, btOutput);

        profile.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void BluetoothSourceProfile_NotAvailable_WhenNothingPaired()
    {
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();
        btInput.IsAvailable.Returns(false);
        btOutput.IsAvailable.Returns(false);
        var profile = new BluetoothSourceProfile(btInput, btOutput);

        profile.IsAvailable.Should().BeFalse();
        profile.UnavailableReason.Should().Be("(no device paired)");
    }

    [Fact]
    public void CustomSourceProfile_IsAlwaysAvailable()
    {
        var profile = new CustomSourceProfile();

        profile.Id.Should().Be("custom");
        profile.IsAvailable.Should().BeTrue();
        profile.UnavailableReason.Should().BeNull();
        profile.FallbackPriority.Should().Be(0);
        profile.Order.Should().Be(100);
    }

    [Fact]
    public void Profiles_HaveDistinctIds()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();

        ISourceProfile[] profiles =
        [
            new PhoneSourceProfile(),
            new LaptopSourceProfile(),
            new HeyCyanSourceProfile(session),
            new BluetoothSourceProfile(btInput, btOutput),
            new CustomSourceProfile(),
        ];

        profiles.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Profiles_OrderedCorrectly()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();

        ISourceProfile[] profiles =
        [
            new CustomSourceProfile(),
            new PhoneSourceProfile(),
            new HeyCyanSourceProfile(session),
            new BluetoothSourceProfile(btInput, btOutput),
        ];

        profiles.OrderBy(p => p.Order).Select(p => p.Id).Should()
            .ContainInOrder("phone", "heycyan-glasses", "bluetooth", "custom");
    }

    [Fact]
    public void HeyCyanSourceProfile_HasHighestFallbackPriority()
    {
        var session = Substitute.For<IHeyCyanGlassesSession>();
        var btInput = Substitute.For<IBluetoothAudioInputProvider>();
        var btOutput = Substitute.For<IBluetoothAudioOutputProvider>();

        ISourceProfile[] profiles =
        [
            new PhoneSourceProfile(),
            new HeyCyanSourceProfile(session),
            new BluetoothSourceProfile(btInput, btOutput),
            new CustomSourceProfile(),
        ];

        profiles.OrderByDescending(p => p.FallbackPriority).First().Id
            .Should().Be("heycyan-glasses");
    }
}
