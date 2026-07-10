using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SingBoot;

internal static class NetworkReadiness
{
    public static bool HasUsableIpv4DefaultGateway()
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                if (!HasUsableIpv4Address(properties) || !HasUsableIpv4Gateway(properties))
                    continue;

                return true;
            }
        }
        catch (NetworkInformationException)
        {
            // Network state can change while interfaces are being enumerated.
            // Treat that as not ready and let the caller retry.
        }

        return false;
    }

    private static bool HasUsableIpv4Address(IPInterfaceProperties properties)
    {
        foreach (var addressInformation in properties.UnicastAddresses)
        {
            var address = addressInformation.Address;
            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                continue;

            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4 && !(bytes[0] == 169 && bytes[1] == 254))
                return true;
        }

        return false;
    }

    private static bool HasUsableIpv4Gateway(IPInterfaceProperties properties)
    {
        foreach (var gatewayInformation in properties.GatewayAddresses)
        {
            var address = gatewayInformation.Address;
            if (address.AddressFamily != AddressFamily.InterNetwork)
                continue;

            var bytes = address.GetAddressBytes();
            if (bytes.Length == 4 && (bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0))
                return true;
        }

        return false;
    }
}
