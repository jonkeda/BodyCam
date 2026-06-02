using System.Net;
using BodyCam.Platforms.Windows.HeyCyan;
using FluentAssertions;

namespace BodyCam.Tests.Services.Glasses.HeyCyan;

public sealed class WindowsTransferEndpointCandidateTests
{
    [Fact]
    public void BuildTransferEndpointCandidates_orders_ble_route_endpoint_pairs_then_known_p2p_hosts()
    {
        var candidates = WindowsHeyCyanGlassesSession.BuildTransferEndpointCandidates(
            IPAddress.Parse("192.168.49.1"),
            IPAddress.Parse("192.168.49.183"),
            new[]
            {
                new WindowsWiFiDirectEndpointPair(
                    LocalHost: "192.168.49.10",
                    LocalService: "0",
                    RemoteHost: "192.168.49.200",
                    RemoteService: "0")
            },
            includeKnownP2pCandidates: true);

        candidates.Select(ip => ip.ToString()).Should().Equal(
            "192.168.49.1",
            "192.168.49.183",
            "192.168.49.200");
    }

    [Fact]
    public void BuildTransferEndpointCandidates_ignores_invalid_endpoint_pair_hosts()
    {
        var candidates = WindowsHeyCyanGlassesSession.BuildTransferEndpointCandidates(
            bleReportedIp: null,
            routeIp: IPAddress.Parse("192.168.49.183"),
            endpointPairs: new[]
            {
                new WindowsWiFiDirectEndpointPair(
                    LocalHost: "192.168.49.10",
                    LocalService: "0",
                    RemoteHost: "not-an-ip",
                    RemoteService: "0")
            },
            includeKnownP2pCandidates: false);

        candidates.Select(ip => ip.ToString()).Should().Equal("192.168.49.183");
    }

    [Fact]
    public void BuildTransferEndpointCandidates_only_adds_known_p2p_hosts_when_requested()
    {
        var withoutKnown = WindowsHeyCyanGlassesSession.BuildTransferEndpointCandidates(
            bleReportedIp: null,
            routeIp: IPAddress.Parse("192.168.4.1"),
            endpointPairs: null,
            includeKnownP2pCandidates: false);

        var withKnown = WindowsHeyCyanGlassesSession.BuildTransferEndpointCandidates(
            bleReportedIp: null,
            routeIp: IPAddress.Parse("192.168.4.1"),
            endpointPairs: null,
            includeKnownP2pCandidates: true);

        withoutKnown.Select(ip => ip.ToString()).Should().Equal("192.168.4.1");
        withKnown.Select(ip => ip.ToString()).Should().Equal(
            "192.168.4.1",
            "192.168.49.183",
            "192.168.49.200",
            "192.168.49.1");
    }
}
