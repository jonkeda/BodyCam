using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

internal static class WindowsTopology
{
    public static IReadOnlyList<WindowsTopologyInterface> Capture()
    {
        var interfaces = new List<WindowsTopologyInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }

            var gateways = properties.GatewayAddresses
                .Select(g => g.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var address in properties.UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(address.Address))
                    continue;

                interfaces.Add(new WindowsTopologyInterface
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Address = address.Address.ToString(),
                    PrefixLength = address.PrefixLength,
                    Gateways = gateways,
                });
            }
        }

        return interfaces;
    }

    public static string ToReadableString(IReadOnlyList<WindowsTopologyInterface> interfaces)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows topology");

        if (interfaces.Count == 0)
        {
            sb.AppendLine("- no active IPv4 interfaces");
            return sb.ToString();
        }

        foreach (var item in interfaces)
        {
            var gateways = item.Gateways.Count == 0
                ? "no gateway"
                : string.Join(", ", item.Gateways);
            sb.AppendLine($"- {item.Name}: {item.Address}/{item.PrefixLength} ({gateways})");
        }

        return sb.ToString();
    }
}

internal sealed class WindowsTopologyInterface
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Address { get; init; }

    public int PrefixLength { get; init; }

    public IReadOnlyList<string> Gateways { get; init; } = [];
}
