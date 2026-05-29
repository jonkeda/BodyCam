#if WINDOWS
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using BodyCam.Services.Camera.A9.Vue990;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BodyCam.RealTests.A9;

public class A9WindowsNativeVue990RealTests
{
    private readonly ITestOutputHelper _output;

    public A9WindowsNativeVue990RealTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task WindowsVue990Status_FetchesIdentityAndServerParameter()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(
            Environment.GetEnvironmentVariable("A9_WINDOWS_PPCS_E2E") == "1",
            "A9_WINDOWS_PPCS_E2E not set to 1");

        var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
        var requiredPrefix = Environment.GetEnvironmentVariable("A9_WINDOWS_CAMERA_SUBNET") ?? "192.168.168.";

        Skip.If(
            !HasLocalAddressPrefix(requiredPrefix),
            $"Windows is not on the expected camera subnet prefix {requiredPrefix}.");

        var status = await new A9Vue990StatusClient().GetStatusAsync(new A9Vue990StatusOptions
        {
            Host = host,
            Username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin",
            Password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888",
            Timeout = TimeSpan.FromSeconds(5),
        });

        SaveArtifact(status, "a9-windows-vue990-status-realtest");
        _output.WriteLine(status.ToReadableString());

        status.Success.Should().BeTrue(status.Error);
        status.DeviceId.Should().Be("BKGD00000100FMQLN");
        status.RealDeviceId.Should().Be("BK0025644WBPD");
        status.Alias.Should().Be("BK7252N");
        status.Server.Should().NotBeNullOrWhiteSpace();
        status.Server.Should().StartWith("DAS-", "the managed PPCS client needs this server parameter next");
    }

    [SkippableFact, Trait("Category", "RealHardware")]
    public async Task WindowsVue990PpcsTransport_FingerprintsCandidatePorts()
    {
        Skip.IfNot(A9RealTestSettings.Enabled, "A9_E2E not set to 1");
        Skip.IfNot(
            Environment.GetEnvironmentVariable("A9_WINDOWS_PPCS_E2E") == "1",
            "A9_WINDOWS_PPCS_E2E not set to 1");

        var host = Environment.GetEnvironmentVariable("A9_CAMERA_IP") ?? "192.168.168.1";
        var requiredPrefix = Environment.GetEnvironmentVariable("A9_WINDOWS_CAMERA_SUBNET") ?? "192.168.168.";

        Skip.If(
            !HasLocalAddressPrefix(requiredPrefix),
            $"Windows is not on the expected camera subnet prefix {requiredPrefix}.");

        var result = await new A9Vue990PpcsTransportProbeClient().ProbeAsync(new A9Vue990PpcsTransportProbeOptions
        {
            Host = host,
            Username = Environment.GetEnvironmentVariable("A9_CAMERA_USERNAME") ?? "admin",
            Password = Environment.GetEnvironmentVariable("A9_CAMERA_PASSWORD") ?? "888888",
            StatusTimeout = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromMilliseconds(1200),
            ReadTimeout = TimeSpan.FromMilliseconds(750),
            MaxBytes = 4096,
        });

        SaveArtifact(result, "a9-windows-vue990-transport-realtest");
        _output.WriteLine(result.ToReadableString());

        result.Success.Should().BeTrue(result.Error);
        result.Status.Should().NotBeNull();
        result.Status!.Success.Should().BeTrue(result.Status.Error);
        result.Das.Should().NotBeNull();
        result.Das!.HasKnownMagic.Should().BeTrue();
        result.Attempts.Should().NotBeEmpty();
        result.Attempts.Should().Contain(attempt => attempt.Protocol == A9Vue990PpcsTransportProtocol.Tcp);
        result.Attempts.Should().Contain(attempt => attempt.Protocol == A9Vue990PpcsTransportProtocol.Udp);
    }

    private static bool HasLocalAddressPrefix(string prefix)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IPAddress.IsLoopback(address.Address))
                    continue;

                if (address.Address.ToString().StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static void SaveArtifact(A9Vue990StatusResult status, string prefix)
    {
        var root = FindRepoRoot();
        if (root is null)
            return;

        var artifactDir = Environment.GetEnvironmentVariable("A9_WINDOWS_REALTEST_ARTIFACT_DIR") ??
            Path.Combine(root, ".my", "plan", "m38-a9-camera", "captures");
        Directory.CreateDirectory(artifactDir);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
        File.WriteAllText(
            Path.Combine(artifactDir, $"{prefix}-{stamp}.json"),
            JsonSerializer.Serialize(status, jsonOptions));
        File.WriteAllText(
            Path.Combine(artifactDir, $"{prefix}-{stamp}.txt"),
            status.ToReadableString());
    }

    private static void SaveArtifact(A9Vue990PpcsTransportProbeResult result, string prefix)
    {
        var root = FindRepoRoot();
        if (root is null)
            return;

        var artifactDir = Environment.GetEnvironmentVariable("A9_WINDOWS_REALTEST_ARTIFACT_DIR") ??
            Path.Combine(root, ".my", "plan", "m38-a9-camera", "captures", "phase-23");
        Directory.CreateDirectory(artifactDir);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd-HHmmss");
        File.WriteAllText(
            Path.Combine(artifactDir, $"{prefix}-{stamp}.json"),
            JsonSerializer.Serialize(result, jsonOptions));
        File.WriteAllText(
            Path.Combine(artifactDir, $"{prefix}-{stamp}.txt"),
            result.ToReadableString());
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".my", "plan", "m38-a9-camera")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
#endif
