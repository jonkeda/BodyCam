using System.Text;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;

namespace BodyCam.Tests.Services.Camera.A9.Vue990;

public class A9Vue990CgiCommandBuilderTests
{
    [Fact]
    public void BuildLiveStreamRequest_WritesExpectedHeaderAndHttpPayload()
    {
        var frame = A9Vue990CgiCommandBuilder.BuildLiveStreamRequest(sequence: 8);

        frame[0].Should().Be(0xd1);
        frame[1].Should().Be(0x00);
        frame[2].Should().Be(0x00);
        frame[3].Should().Be(0x08);
        frame[4].Should().Be(0x01);
        frame[5].Should().Be(0x0a);
        frame[8..12].Should().AllBeEquivalentTo((byte)0x00);

        var payloadLength = (frame[6] << 8) | frame[7];
        payloadLength.Should().Be(frame.Length - 12);
        Encoding.ASCII.GetString(frame[12..]).Should()
            .Be("GET /livestream.cgi?streamid=10&substream=0& HTTP/1.1\r\n\r\n");
    }

    [Fact]
    public void TryReadHeader_ParsesBuiltRequest()
    {
        var frame = A9Vue990CgiCommandBuilder.BuildGetRequest("/get_status.cgi", sequence: 42);

        var parsed = A9Vue990CgiCommandBuilder.TryReadHeader(
            frame,
            out var sequence,
            out var payloadLength);

        parsed.Should().BeTrue();
        sequence.Should().Be(42);
        ((int)payloadLength).Should().Be(frame.Length - 12);
    }

    [Fact]
    public void BuildGetRequest_CanWritePayloadWithoutLeadingSlash()
    {
        var frame = A9Vue990CgiCommandBuilder.BuildGetRequest(
            "/livestream.cgi?streamid=10&substream=0&",
            sequence: 1,
            leadingSlash: false);

        Encoding.ASCII.GetString(frame[12..]).Should()
            .Be("GET livestream.cgi?streamid=10&substream=0& HTTP/1.1\r\n\r\n");
    }

    [Fact]
    public void BuildRawCgiPathPayload_WritesOptionalNullTerminator()
    {
        var payload = A9Vue990CgiCommandBuilder.BuildRawCgiPathPayload(
            "/livestream.cgi?streamid=10&substream=0&",
            nullTerminated: true);

        Encoding.ASCII.GetString(payload[..^1]).Should()
            .Be("livestream.cgi?streamid=10&substream=0&");
        payload[^1].Should().Be(0);
    }

    [Fact]
    public void BuildNativeLiveStreamCgiCommandBody_WritesNativeWriteCgiBody()
    {
        var body = A9Vue990CgiCommandBuilder.BuildNativeLiveStreamCgiCommandBody("admin", "888888");

        Encoding.ASCII.GetString(body).Should().Be(
            "GET /livestream.cgi?streamid=10&substream=0&loginuse=admin&loginpas=888888&user=admin&pwd=888888&");
    }

    [Fact]
    public void BuildNativeLoginStatusCgiCommandBody_WritesNativeLoginProbeBody()
    {
        var body = A9Vue990CgiCommandBuilder.BuildNativeLoginStatusCgiCommandBody("admin", "888888");

        Encoding.ASCII.GetString(body).Should().Be(
            "GET /get_status.cgi?name=admin&loginuse=admin&loginpas=888888&user=admin&pwd=888888&");
    }

    [Fact]
    public void BuildNativeCgiCommandHeader_WritesLittleEndianCommandHeader()
    {
        var header = A9Vue990CgiCommandBuilder.BuildNativeCgiCommandHeader(97);

        header.Should().Equal(
            0x01,
            0x0a,
            0x00,
            0x00,
            0x61,
            0x00,
            0x00,
            0x00);
    }

    [Fact]
    public void TryReadNativeCgiCommandHeader_ParsesNativeHeader()
    {
        var header = A9Vue990CgiCommandBuilder.BuildNativeCgiCommandHeader(97);

        var parsed = A9Vue990CgiCommandBuilder.TryReadNativeCgiCommandHeader(
            header,
            out var commandId,
            out var payloadLength);

        parsed.Should().BeTrue();
        commandId.Should().Be(A9Vue990CgiCommandBuilder.NativeCgiCommandId);
        payloadLength.Should().Be(97);
    }
}
