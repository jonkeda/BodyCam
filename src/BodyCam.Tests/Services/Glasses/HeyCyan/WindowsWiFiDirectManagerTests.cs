using BodyCam.Platforms.Windows.HeyCyan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public class WindowsWiFiDirectManagerTests
{
    // ── IsLikelyGlassesPeer — positive matches ──────────────────────

    [Theory]
    [InlineData("AIM-Pro-X1")]
    [InlineData("AIMB-2024")]
    [InlineData("SmartGLASS-v2")]
    [InlineData("QC-Smart-Glasses")]
    [InlineData("O_MyDevice")]
    [InlineData("M01-something")]
    [InlineData("HeyCyan-Glasses")]
    [InlineData("MyCyan Device")]
    [InlineData("DIRECT-abc")]
    [InlineData("AABBCCDDEEFF")]           // 12-char hex (MAC-like)
    [InlineData("prefix-D879B87FE6C9-suffix")] // contains 12-char hex
    public void IsLikelyGlassesPeer_KnownPatterns_ReturnsTrue(string name)
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        mgr.IsLikelyGlassesPeer(name).Should().BeTrue(
            $"'{name}' should match a known glasses pattern");
    }

    // ── IsLikelyGlassesPeer — BLE MAC match ────────────────────────

    [Theory]
    [InlineData("D8:79:B8:7F:E6:C9", "D879B87FE6C9")]
    [InlineData("D8:79:B8:7F:E6:C9", "SomePrefix-D879B87FE6C9")]
    [InlineData("D8:79:B8:7F:E6:C9", "d879b87fe6c9")]  // case-insensitive
    public void IsLikelyGlassesPeer_BleMac_ReturnsTrue(string bleMac, string peerName)
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance,
            bleMacAddress: bleMac);

        mgr.IsLikelyGlassesPeer(peerName).Should().BeTrue(
            $"'{peerName}' should match BLE MAC '{bleMac}'");
    }

    // ── IsLikelyGlassesPeer — negative matches ─────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("My-Laptop")]
    [InlineData("iPhone-12")]
    [InlineData("DESKTOP-ABC123")]
    [InlineData("Samsung-TV")]
    [InlineData("Ziggo3168718")]           // household WiFi
    [InlineData("Blacklabel-5G")]
    [InlineData("ABCG1234HIJK")]           // not all hex
    public void IsLikelyGlassesPeer_UnrelatedDevices_ReturnsFalse(string? name)
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        mgr.IsLikelyGlassesPeer(name).Should().BeFalse(
            $"'{name}' should not match any glasses pattern");
    }

    // ── IsLikelyGlassesPeer — BLE MAC mismatch ─────────────────────

    [Fact]
    public void IsLikelyGlassesPeer_WrongMac_ReturnsFalse()
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance,
            bleMacAddress: "D8:79:B8:7F:E6:C9");

        // Random 12-char hex that doesn't match our MAC
        mgr.IsLikelyGlassesPeer("1122334455AA").Should().BeTrue(
            "12-char hex always matches regardless of MAC (MAC match is higher priority)");

        // Non-hex device name with wrong MAC embedded
        mgr.IsLikelyGlassesPeer("Device-112233445566").Should().BeTrue(
            "12-char hex substring match");

        // No hex pattern, no MAC match, no keyword
        mgr.IsLikelyGlassesPeer("My-Laptop-Pro").Should().BeFalse();
    }

    // ── IsLikelyGlassesPeer — case insensitivity ────────────────────

    [Theory]
    [InlineData("aim-glasses")]
    [InlineData("Aim-Pro")]
    [InlineData("qc-smart")]
    [InlineData("direct-abc")]
    [InlineData("heycyan")]
    [InlineData("glass-device")]
    public void IsLikelyGlassesPeer_CaseInsensitive(string name)
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        mgr.IsLikelyGlassesPeer(name).Should().BeTrue(
            $"'{name}' should match case-insensitively");
    }

    // ── Initial state ───────────────────────────────────────────────

    [Fact]
    public void NewManager_IsNotConnected()
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        mgr.IsConnected.Should().BeFalse();
        mgr.RemoteIp.Should().BeNull();
    }

    [Fact]
    public void Disconnect_WhenNotConnected_DoesNotThrow()
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        var act = () => mgr.Disconnect();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_WhenNotConnected_DoesNotThrow()
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance);

        var act = () => mgr.Dispose();

        act.Should().NotThrow();
    }

    // ── MAC normalization ───────────────────────────────────────────

    [Theory]
    [InlineData("D8:79:B8:7F:E6:C9")]
    [InlineData("D8-79-B8-7F-E6-C9")]
    [InlineData("d879b87fe6c9")]
    [InlineData("D879B87FE6C9")]
    public void BleMac_NormalizedFormats_AllMatch(string macFormat)
    {
        var mgr = new WindowsWiFiDirectManager(
            NullLogger<WindowsWiFiDirectManager>.Instance,
            bleMacAddress: macFormat);

        mgr.IsLikelyGlassesPeer("D879B87FE6C9").Should().BeTrue(
            $"MAC format '{macFormat}' should be normalized and match");
    }
}
